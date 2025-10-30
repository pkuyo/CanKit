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

    internal event Action<IsoTpChannelCore.TxOperation>? TxOperationTransmitted;

    internal event Action<IsoTpChannelCore.TxOperation, Exception>? TxOperationExceptionOccurred;

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

    public void TransmitWithAs(in IsoTpChannelCore.TxOperation operation)
    {
        if (_bus.Transmit(operation.Dequeue()) == 0)
        {
            throw new IsoTpException(IsoTpErrorCode.BusTxRejected);
        }
        else
        {

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
    }

    private void OnBackgroundExceptionOccurred(object? _, Exception e)
    {
        throw new IsoTpException(IsoTpErrorCode.BackgroundException, e.Message, null, e);
    }



    public static ulong ComputeFnv1a64(in CanFdFrame frame)
    {
        const ulong FNV_OFFSET = 1469598103934665603UL;
        const ulong FNV_PRIME = 1099511628211UL;
        ulong h = FNV_OFFSET;

        void MixByte(byte b) { h ^= b; h *= FNV_PRIME; }
        unchecked {
            for (int i = 0; i < 4; i++) MixByte((byte)((frame.ID >> (8*i)) & 0xFF));
        }
        MixByte((byte)((1 << 4) | ((frame.BitRateSwitch ? 1 : 0) << 3) |
                       ((frame.ErrorStateIndicator ? 1 : 0) << 2) |
                       ((frame.IsErrorFrame ? 1 : 0) << 1) |
                       (frame.IsExtendedFrame ? 1 : 0)));
        MixByte(frame.Dlc);

        var span = frame.Data.Span;
        for (int i = 0; i < span.Length; i++) MixByte(span[i]);

        return h;
    }

    public static ulong ComputeFnv1a64(in CanClassicFrame frame)
    {
        const ulong FNV_OFFSET = 1469598103934665603UL;
        const ulong FNV_PRIME = 1099511628211UL;
        ulong h = FNV_OFFSET;

        void MixByte(byte b) { h ^= b; h *= FNV_PRIME; }
        unchecked {
            for (int i = 0; i < 4; i++) MixByte((byte)((frame.ID >> (8*i)) & 0xFF));
        }
        MixByte((byte)(((frame.IsRemoteFrame ? 1 : 0) << 2) |
                       ((frame.IsErrorFrame ? 1 : 0) << 1) |
                       (frame.IsExtendedFrame ? 1 : 0)));
        MixByte(frame.Dlc);

        var span = frame.Data.Span;
        for (int i = 0; i < span.Length; i++) MixByte(span[i]);

        return h;
    }

}
