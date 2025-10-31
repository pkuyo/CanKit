using System.Diagnostics;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Diagnostics;
using CanKit.Protocol.IsoTp.Utils;

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
    private AsyncAutoResetEvent _txOrTimeOutEvent = new();

    internal delegate void TxOperationTransmittedHandle(IsoTpChannelCore.TxOperation operation,
        in IsoTpChannelCore.TxFrame frame);

    internal delegate void TxOperationExceptionOccurredHandle(IsoTpChannelCore.TxOperation operation,
        in IsoTpChannelCore.TxFrame frame, Exception exception);

    internal event TxOperationTransmittedHandle? TxOperationTransmitted;

    internal event TxOperationExceptionOccurredHandle? TxOperationExceptionOccurred;

    public IsoTpScheduler(ICanBus bus, IsoTpOptions options)
    {
        if(options.QueuedCanBusOptions is not null)
            _bus = bus.WithQueuedTx(options.QueuedCanBusOptions);
        else
            _bus = bus;
        _opt = options;
        _canEcho = (_bus.Options.Features & CanFeature.Echo) != 0;
    }

    public void Register(IsoTpChannelCore ch) { _channels.Add(ch); _router.Register(ch); }
    public void Unregister(IsoTpChannelCore ch) { _channels.Remove(ch); _router.Unregister(ch); }

    public void TransmitTxOperation(in IsoTpChannelCore.TxOperation operation)
    {
        var txFrame = operation.Dequeue();
        using var frame = txFrame.Frame;
        if (_bus.Transmit(frame) == 0)
        {
            TxOperationExceptionOccurred?.Invoke(operation, txFrame, new IsoTpException(IsoTpErrorCode.BusTxRejected));
        }
        else
        {
            TxOperationTransmitted?.Invoke(operation, txFrame);
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
                    TransmitTxOperation(pf);
                }
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
                    TransmitTxOperation(operation);
                    _lastDataTxTicks = Stopwatch.GetTimestamp();
                }
            }
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

    private void OnOperationTransmitted(IsoTpChannelCore.TxOperation operation, in IsoTpChannelCore.TxFrame frame)
    {
        _router.Route(operation, frame);
    }

    private void OnTxExceptionOccurred(IsoTpChannelCore.TxOperation operation,
        in IsoTpChannelCore.TxFrame frame, Exception exception)
    {
        _router.Route(operation, frame, exception);
    }

    private void OnFrameReceived(object? sender, CanReceiveData e)
    {
        _router.Route(e);
    }

    private void OnBackgroundExceptionOccurred(object? _, Exception e)
    {
        throw new IsoTpException(IsoTpErrorCode.BackgroundException, e.Message, null, e);
    }
}
