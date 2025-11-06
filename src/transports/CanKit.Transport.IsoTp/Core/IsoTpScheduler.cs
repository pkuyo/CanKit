using System.Diagnostics;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Excpetions;
using CanKit.Protocol.IsoTp.Utils;

namespace CanKit.Transport.IsoTp.Core;


internal sealed class IsoTpScheduler : IIsoTpScheduler
{
    private readonly ICanBus _bus;
    private readonly bool _canEcho;
    private readonly IBusOptions _opt;
    private TimeSpan? _globalBusGuard;
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

    public IsoTpScheduler(ICanBus bus, IBusOptions options)
    {
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

        //TODO:
        await Task.Delay(1);

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
                if (!ch.IsReadyToSendData(now, _globalBusGuard)) continue;
                if (!ch.TryPeekData(out var f))
                    continue;
                var score = Score(ch, f, now);
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
        if (_globalBusGuard is null) return true;
        var elapsed = TimeSpan.FromSeconds((nowTicks - _lastDataTxTicks) / (double)Stopwatch.Frequency);
        return elapsed >= _globalBusGuard.Value;
    }

    private static double Score(IsoTpChannelCore ch, CanFrame f, long nowTicks)
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

    public void Dispose() => _bus.Dispose();
    public IBusRTOptionsConfigurator Options => _bus.Options;
    public BusNativeHandle NativeHandle => _bus.NativeHandle;
    public void AddChannel(IIsoTpChannel channel) => throw new NotImplementedException();

    public void RemoveChannel(IIsoTpChannel channel) => throw new NotImplementedException();
}
