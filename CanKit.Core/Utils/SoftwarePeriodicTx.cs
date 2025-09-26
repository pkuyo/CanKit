using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils;

/// <summary>
/// Software fallback implementation of periodic transmit.
/// 软件定时器的周期发送实现。
/// </summary>
public sealed class SoftwarePeriodicTx : IPeriodicTx
{

    /// <summary>
    /// Start a software periodic task bound to the given bus.
    /// 在指定总线启动一个软件定时的周期任务。
    /// </summary>
    public static IPeriodicTx Start(ICanBus bus, CanTransmitData frame, PeriodicTxOptions options)
    {
        var h = new SoftwarePeriodicTx(bus, frame, options);
        h.Start();
        return h;
    }

    private SoftwarePeriodicTx(ICanBus bus, CanTransmitData frame, PeriodicTxOptions options)
    {
        _bus = bus;
        _frame = frame;
        _period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        _remaining = options.Repeat;
        _repeat = options.Repeat;
        _fireImmediately = options.FireImmediately;
    }

    /// <summary>
    /// Whether the task is currently running.
    /// 指示任务当前是否在运行。
    /// </summary>
    public bool IsRunning => _running;
    public TimeSpan Period => _period;
    public int RepeatCount => _repeat;
    public int RemainingCount => _remaining;

    /// <summary>
    /// Raised when the task finishes due to reaching the repeat count.
    /// 因达到次数而结束时触发。
    /// </summary>
    public event EventHandler? Completed;

    public void Start()
    {
        if (_task != null) return;
        _running = true;
        _task = Task.Run(LoopAsync);
    }

    public void Stop()
    {
        try
        {
            _cts.Cancel();
            _task?.Wait(200);
        }
        catch { }
        finally
        {
            _running = false;
        }
    }

    public void Update(CanTransmitData? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        lock (_gate)
        {
            if (frame is not null) _frame = frame;
            if (period.HasValue && period.Value > TimeSpan.Zero) _period = period.Value;
            if (repeatCount.HasValue)
            {
                _remaining = repeatCount.Value;
                _repeat = repeatCount.Value;
            }
        }
    }

    private async Task LoopAsync()
    {
        var token = _cts.Token;
        try
        {
            if (_fireImmediately)
            {
                TrySendOnce();
                DecreaseAndMaybeFinish();
            }

            while (!token.IsCancellationRequested)
            {
                var delay = _period;
                if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMilliseconds(1);
                try { await Task.Delay(delay, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                TrySendOnce();
                if (DecreaseAndMaybeFinish())
                    break;
            }
        }
        finally
        {
            _running = false;
        }
    }

    private void TrySendOnce()
    {
        try
        {
            CanTransmitData frame;
            lock (_gate) { frame = _frame; }
            _ = _bus.Transmit(new[] { frame });
        }
        catch { /* avoid background failures */ }
    }

    private bool DecreaseAndMaybeFinish()
    {
        lock (_gate)
        {
            if (_remaining == 0)
                return false; // already finite-complete
            if (_remaining > 0)
            {
                _remaining--;
                if (_remaining == 0)
                {
                    Completed?.Invoke(this, EventArgs.Empty);
                    Stop();
                    return true;
                }
            }
        }
        return false;
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private readonly ICanBus _bus;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private Task? _task;

    private CanTransmitData _frame;
    private TimeSpan _period;
    private int _remaining; // -1 for infinite
    private readonly bool _fireImmediately;
    private volatile bool _running;
    private int _repeat;
}


