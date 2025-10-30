using System.Diagnostics;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Diagnostics;

namespace CanKit.Protocol.IsoTp.Core;


internal sealed class IsoTpScheduler
{
    private readonly ICanBus _bus;
    private readonly bool _canEcho;
    private readonly IsoTpOptions _opt;
    private readonly Router _router = new();
    private readonly List<IsoTpChannelCore> _channels = new();
    private readonly List<(double score, IsoTpChannelCore ch)> _candidates = new();
    private long _lastDataTxTicks;

    private Stopwatch _asStopWatch = new();
    private ManualResetEvent _ackEvent = new(false);
    private ICanFrame? _ackFrame;

    internal event Action<IsoTpChannelCore.TxOperation>? TxOperationTransmitted;

    internal event Action<IsoTpChannelCore.TxOperation, Exception>? TxOperationExceptionOccurred;

    public IsoTpScheduler(ICanBus bus, IsoTpOptions opt)
    {
        _bus = bus;
        _opt = opt;
        _canEcho = (_bus.Options.Features & CanFeature.Echo) != 0;
    }

    public void Register(IsoTpChannelCore ch) { _channels.Add(ch); _router.Register(ch); }
    public void Unregister(IsoTpChannelCore ch) { _channels.Remove(ch); _router.Unregister(ch); }

    public void TransmitWithAs(in IsoTpChannelCore.TxOperation operation)
    {
        try
        {
            _asStopWatch.Restart();
            _ackFrame = operation.Dequeue();
            while (true)
            {
                if (_bus.Transmit(_ackFrame) == 1)
                {
                    if (_canEcho && !_ackEvent.WaitOne(_opt.N_As - _asStopWatch.Elapsed))
                    {
                        TxOperationExceptionOccurred?.Invoke
                            (operation, new IsoTpException(IsoTpErrorCode.Timeout_N_As, "TODO"));
                    }
                    break;
                }

                PreciseDelay.Delay(TimeSpan.FromMilliseconds(1));
                if (_asStopWatch.Elapsed > _opt.N_As)
                {
                    TxOperationExceptionOccurred?.Invoke
                        (operation, new IsoTpException(IsoTpErrorCode.Timeout_N_As, "TODO"));
                }
            }

            if (operation.Empty)
            {
                TxOperationTransmitted?.Invoke(operation);
            }
        }
        catch (Exception e)
        {
            operation.Dispose();
            TxOperationExceptionOccurred?.Invoke(operation, e);
        }
        finally
        {
            _asStopWatch.Stop();
            _ackEvent.Reset();
            _ackFrame = null;
        }

    }

    public async Task RunAsync(CancellationToken ct)
    {
        _bus.FrameReceived += OnFrameReceived;
        _bus.BackgroundExceptionOccurred += OnBackgroundExceptionOccurred;
        TxOperationTransmitted += OnOperationTransmitted;
        TxOperationExceptionOccurred += OnTxExceptionOccurred;


        while (!ct.IsCancellationRequested)
        {
            // FC优先
            foreach (var ch in _channels)
            {
                while (true)
                {
                    using var pf = ch.TryDequeueFC();
                    if (pf is null)
                        break;
                    TransmitWithAs(pf);
                }
            }

            // 2) 定时器巡检
            foreach (var ch in _channels)
            {
                ch.ProcessTimers();
            }


            _candidates.Clear();
            var now = Stopwatch.GetTimestamp();
            foreach (var ch in _channels)
            {
                if (!ch.IsReadyToSendData(now, _opt.GlobalBusGuard)) continue;
                if (!ch.TryPeekData(out var f))
                    continue;
                var score = Score(ch, f!, now);
                _candidates.Add((score, ch));
            }


            if (_candidates.Count > 0 && RespectBusGuard(now))
            {
                _candidates.Sort((a, b) => b.score.CompareTo(a.score));
                var (score, ch) = _candidates[0];
                if (ch.TryPeekOperation(out var operation))
                {
                    TransmitWithAs(operation);
                    _lastDataTxTicks = Stopwatch.GetTimestamp();
                }
            }


            await Task.Yield();
        }
    }



    private bool RespectBusGuard(long nowTicks)
    {
        if (_opt.GlobalBusGuard is null) return true;
        var elapsed = TimeSpan.FromSeconds((nowTicks - _lastDataTxTicks) / (double)Stopwatch.Frequency);
        return elapsed >= _opt.GlobalBusGuard.Value;
    }

    private static double Score(IsoTpChannelCore ch, ICanFrame f, long nowTicks)
    {
        // TODO:优先级计算
        return nowTicks * 1e-12;
    }

    private void OnOperationTransmitted(IsoTpChannelCore.TxOperation operation)
    {
        _router.Route(operation);
    }

    private void OnTxExceptionOccurred(IsoTpChannelCore.TxOperation operation, Exception exception)
    {
        _router.Route(operation, exception);
    }

    private void OnFrameReceived(object? sender, CanReceiveData e)
    {
        _router.Route(e);
        if (_canEcho && e.IsEcho && _ackFrame is not null)
        {
            if (e.CanFrame.ID == _ackFrame.ID && e.CanFrame.Data.Span.SequenceEqual(_ackFrame.Data.Span))
            {
                _ackEvent.Set();
            }
        }
    }

    private void OnBackgroundExceptionOccurred(object? _, Exception e)
    {
        throw new IsoTpException(IsoTpErrorCode.BackgroundException, e.Message, null, e);
    }
}
