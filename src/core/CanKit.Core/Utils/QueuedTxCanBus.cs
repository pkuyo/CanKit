using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils;
/// <summary>
/// 队列满时的策略：
/// DropOldest  —— 丢弃最老的队列元素以容纳新元素（高实时性）
/// ReturnPartial —— 不丢弃旧元素；尽量接纳本次写入的一部分，返回已接纳数量（保守有序）
/// </summary>
public enum TxQueueFullPolicy
{
    DropOldest,
    ReturnPartial
}

public sealed class QueuedCanBusOptions
{
    public int Capacity { get; init; } = 1024;
    public int SendBatchSize { get; init; } = 64;
    public TxQueueFullPolicy FullPolicy { get; init; } = TxQueueFullPolicy.ReturnPartial;

    public TimeSpan BackoffInitial { get; init; } = TimeSpan.FromMilliseconds(1);
    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromMilliseconds(8);
    public double BackoffFactor { get; init; } = 2.0;

    public bool OwnsInnerBus { get; init; } = false;
}

/// <summary>
/// 基于 Channel 的发送队列包装器。
/// - ICanBus 的所有读取&状态接口透明转发；
/// - 所有 Transmit/TransmitAsync 改为“入队 + 后台发送”，返回“已入队数量”（而非驱动已接受数量）；
/// - 提供 ResetTxRequestCycle() 手动重置回退并立刻唤醒。
/// </summary>
public sealed class QueuedCanBus : ICanBus, IAsyncDisposable
{
    private readonly ICanBus _inner;
    private readonly QueuedCanBusOptions _opts;

    private readonly Channel<ICanFrame> _txChan;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    // 指数回退状态
    private TimeSpan _backoff;
    private CancellationTokenSource _sleepCts = new(); // 用于打断回退等待

    // 统计
    private long _enqOk, _enqDrop, _drvAccepted, _drvBusy;

    public QueuedCanBus(ICanBus inner, QueuedCanBusOptions? opts = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _opts = opts ?? new();

        var bco = new BoundedChannelOptions(_opts.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = _opts.FullPolicy == TxQueueFullPolicy.DropOldest
                ? BoundedChannelFullMode.DropOldest
                : BoundedChannelFullMode.DropWrite
        };
        _txChan = Channel.CreateBounded<ICanFrame>(bco);

        _backoff = _opts.BackoffInitial;
        _worker = Task.Run(SendLoop, _cts.Token);

        // 透明转发底层事件
        _inner.FrameReceived += (s, e) => FrameReceived?.Invoke(this, e);
        _inner.ErrorFrameReceived += (s, e) => ErrorFrameReceived?.Invoke(this, e);
        _inner.BackgroundExceptionOccurred += (s, e) => BackgroundExceptionOccurred?.Invoke(this, e);
    }

    #region ICanBus

    public IBusRTOptionsConfigurator Options => _inner.Options;
    public BusState BusState => _inner.BusState;
    public BusNativeHandle NativeHandle => _inner.NativeHandle;

    public void Reset() => _inner.Reset();
    public void ClearBuffer() => _inner.ClearBuffer();

    public float BusUsage() => _inner.BusUsage();
    public CanErrorCounters ErrorCounters() => _inner.ErrorCounters();

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
        => _inner.Receive(count, timeOut);

    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0,
        CancellationToken cancellationToken = default)
        => _inner.ReceiveAsync(count, timeOut, cancellationToken);


    public IAsyncEnumerable<CanReceiveData> GetFramesAsync(CancellationToken cancellationToken = default)
        => _inner.GetFramesAsync(cancellationToken);

    public IPeriodicTx TransmitPeriodic(ICanFrame frame, PeriodicTxOptions options)
        => _inner.TransmitPeriodic(frame, options);

    public event EventHandler<CanReceiveData>? FrameReceived;
    public event EventHandler<ICanErrorInfo>? ErrorFrameReceived;
    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    #endregion

    #region Transmit

    /// <summary>
    /// 注意：本包装的 Transmit 返回“成功入队的数量”，并非“驱动立即接受的数量”。
    /// 真正的下发由内部发送线程异步完成。
    /// </summary>
    public int Transmit(IEnumerable<ICanFrame> frames, int timeOut = 0)
        => EnqueueMany(frames);

    public int Transmit(ReadOnlySpan<ICanFrame> frames, int timeOut = 0)
        => EnqueueMany(frames);

    public int Transmit(ICanFrame[] frames, int timeOut = 0)
        => EnqueueMany(frames.AsSpan());

    public int Transmit(ArraySegment<ICanFrame> frames, int timeOut = 0)
        => EnqueueMany(frames.AsSpan());

    public int Transmit(in ICanFrame frame)
        => EnqueueOne(frame);

    public Task<int> TransmitAsync(IEnumerable<ICanFrame> frames, int timeOut = 0,
        CancellationToken cancellationToken = default)
        => Task.FromResult(EnqueueMany(frames));

    public Task<int> TransmitAsync(ICanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(EnqueueOne(frame));

    private int EnqueueOne(in ICanFrame frame)
    {
        var ok = _txChan.Writer.TryWrite(frame);
        if (ok) Interlocked.Increment(ref _enqOk);
        else
        {
            if (_opts.FullPolicy == TxQueueFullPolicy.DropOldest)
            {
                ok = _txChan.Writer.TryWrite(frame);
                if (ok) Interlocked.Increment(ref _enqOk);
                else Interlocked.Increment(ref _enqDrop);
            }
            else
            {
                // ReturnPartial：不阻塞，不丢旧，直接报告 0
                Interlocked.Increment(ref _enqDrop);
            }
        }
        if (ok) KickWorker();
        return ok ? 1 : 0;
    }

    private int EnqueueMany(IEnumerable<ICanFrame> frames)
    {
        int accepted = 0;
        foreach (var f in frames)
            accepted += EnqueueOne(f);
        return accepted;
    }

    private int EnqueueMany(ReadOnlySpan<ICanFrame> frames)
    {
        int accepted = 0;
        for (int i = 0; i < frames.Length; i++)
            accepted += EnqueueOne(frames[i]);
        return accepted;
    }

    #endregion

    #region TxWorker

    private async Task SendLoop()
    {
        var token = _cts.Token;
        var batch = new ICanFrame[_opts.SendBatchSize];
        var index = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await _txChan.Reader.WaitToReadAsync(token)) break;
                while (index < _opts.SendBatchSize && _txChan.Reader.TryRead(out var f))
                    batch[index++] = f;

                if (index == 0) continue;

                int accepted = _inner.Transmit(batch, timeOut: 0);
                if (accepted > 0)
                {
                    Interlocked.Add(ref _drvAccepted, accepted);
                    for (var i = 0; i < accepted; i++)
                    {
                        batch[i].Dispose();
                    }
                    if (accepted < index)
                    {
                        for (int i = accepted; i < index; i++)
                        {
                            batch[i - accepted] = batch[i];
                        }
                    }

                    index -= accepted;
                    ResetBackoffInternal();
                    continue;
                }
                Interlocked.Increment(ref _drvBusy);
                await SleepWithResetAsync(NextBackoff(), token);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                /* 正常退出 */
            }
            catch (Exception ex)
            {
                // 把异常上抛给调用者订阅
                BackgroundExceptionOccurred?.Invoke(this, ex);
                // 轻量策略：小退后重试避免旋转异常
                await Task.Delay(5, token);
            }
        }
    }

    private TimeSpan NextBackoff()
    {
        var b = _backoff;
        var nextTicks = Math.Min((long)(_backoff.Ticks * _opts.BackoffFactor), _opts.BackoffMax.Ticks);
        _backoff = TimeSpan.FromTicks(nextTicks);
        return b;
    }

    private void ResetBackoffInternal()
    {
        _backoff = _opts.BackoffInitial;
    }

    private async Task SleepWithResetAsync(TimeSpan delay, CancellationToken token)
    {
        var old = Interlocked.Exchange(ref _sleepCts, new CancellationTokenSource());
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _sleepCts.Token);
            try
            {
                await PreciseDelay.DelayAsync(delay, ct:linked.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }
        finally
        {
            old.Cancel();
            old.Dispose();
        }
    }

    private void KickWorker()
    {
        _sleepCts.Cancel();
    }

    /// <summary>
    /// 手动重置 TX 请求周期：重置回退并立刻唤醒发送线程。
    /// 典型用法：收到对端 FC / 发现底层恢复可用时调用。
    /// </summary>
    public void ResetTxRequestCycle()
    {
        ResetBackoffInternal();
        KickWorker();
    }

    #endregion


    public void Dispose()
    {
        _cts.Cancel();
        _txChan.Writer.TryComplete();
        try
        {
            _worker.Wait();
        }
        catch
        { /* ignore */ }

        if (_opts.OwnsInnerBus) _inner.Dispose();
        _cts.Dispose();
        _sleepCts.Cancel();
        _sleepCts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _txChan.Writer.TryComplete();
        try
        {
            await _worker;
        }
        catch
        { /* ignore */ }

        if (_opts.OwnsInnerBus) _inner.Dispose();
        _cts.Dispose();
        _sleepCts.Cancel();
        _sleepCts.Dispose();
    }

    public (long Enqueued, long Dropped, long DrvAccepted, long DrvBusy) GetTxStats()
        => (Interlocked.Read(ref _enqOk), Interlocked.Read(ref _enqDrop), Interlocked.Read(ref _drvAccepted),
            Interlocked.Read(ref _drvBusy));
}
