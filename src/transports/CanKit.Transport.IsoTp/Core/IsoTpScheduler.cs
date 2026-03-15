using System.Diagnostics;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Excpetions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Utils;

namespace CanKit.Transport.IsoTp.Core;

/// <summary>
/// ISOTP总线调度器
/// </summary>
internal sealed class IsoTpScheduler
{

    private readonly ICanBus _bus;
    private readonly IBusOptions _opt;
    private TimeSpan? _globalBusGuard;
    private readonly Router _router = new();
    private readonly List<IsoTpChannelCore> _channels = new();

    private TaskCompletionSource<bool>? _waitAnyDeadline;
    private AsyncAutoResetEvent _waitAnyTxOperation = new();
    internal delegate void TxOperationTransmittedHandle(OutboundItem item);

    internal delegate void TxOperationExceptionOccurredHandle(OutboundItem item, Exception exception);

    internal event TxOperationTransmittedHandle? TxOperationTransmitted;

    internal event TxOperationExceptionOccurredHandle? TxOperationExceptionOccurred;

    public IsoTpScheduler(ICanBus bus, IBusOptions options)
    {
        _bus = bus;
        _opt = options;
    }

    public void Register(IsoTpChannelCore ch)
    {
        _channels.Add(ch);
        _router.Register(ch);
        UpdateGlobalBusGuard(ch.Options.GlobalBusGuard);
    }

    public void Unregister(IsoTpChannelCore ch)
    {
        _channels.Remove(ch);
        _router.Unregister(ch);
        RecalculateGlobalBusGuard();
    }

    public void TransmitTxOperation(in OutboundItem item)
    {
        using var frame = item.Frame;
        if (_bus.Transmit(frame) == 0)
        {
            TxOperationExceptionOccurred?.Invoke(item, new IsoTpException(IsoTpErrorCode.BusTxRejected));
        }
        else
        {
            TxOperationTransmitted?.Invoke(item);
        }
    }

    public async Task TimeOutScheduler(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_channels.Count == 0)
            {
                _waitAnyDeadline = new TaskCompletionSource<bool>();
                ct.Register(() => _waitAnyDeadline.TrySetResult(true));
                await _waitAnyDeadline.Task;
                continue;
            }

            var minExpiryTime = TimeSpan.MaxValue;
            var now = Stopwatch.GetTimestamp();
            foreach (var channel in _channels)
            {
                var tmp = channel.GetNextExpiryTime(now, _globalBusGuard);
                minExpiryTime = tmp < minExpiryTime ? tmp : minExpiryTime;
            }
            await PreciseDelay.DelayAsync(minExpiryTime, ct: ct);
        }
    }

    public async Task TxScheduler(CancellationToken ct)
    {
        _bus.FrameReceived += OnFrameReceived;
        _bus.BackgroundExceptionOccurred += OnBackgroundExceptionOccurred;
        TxOperationTransmitted += OnOperationTransmitted;
        TxOperationExceptionOccurred += OnTxExceptionOccurred;

        while (!ct.IsCancellationRequested)
        {
            _waitAnyTxOperation.Reset();
            var now = Stopwatch.GetTimestamp();
            var minTime = TimeSpan.MaxValue;
            foreach (var ch in _channels)
            {
                TimeSpan waitTime;
                while (ch.TryDequeueReadyFrame(now, _globalBusGuard, out OutboundItem item,out waitTime))
                {
                    TransmitTxOperation(item);
                }
                minTime = minTime > waitTime ? waitTime : minTime;
            }
            await Task.WhenAny(_waitAnyTxOperation.WaitAsync(), PreciseDelay.DelayAsync(minTime, ct: ct));
        }
    }
    private void OnWorkAvailable() => _waitAnyTxOperation.Set();

    private void OnOperationTransmitted(OutboundItem item)
    {
        _router.Route(item);
    }

    private void OnTxExceptionOccurred(OutboundItem item, Exception exception)
    {
        _router.Route(item);
    }

    private void OnFrameReceived(object? sender, CanReceiveData e)
    {
        _router.Route(e);
    }

    private void OnBackgroundExceptionOccurred(object? _, Exception e)
    {
        //TODO: 异常处理
        throw new IsoTpException(IsoTpErrorCode.BackgroundException, e.Message, null, e);
    }

    private void UpdateGlobalBusGuard(TimeSpan? requested)
    {
        if (requested is null || requested.Value <= TimeSpan.Zero) return;
        if (_globalBusGuard is null || requested > _globalBusGuard)
        {
            _globalBusGuard = requested;
        }
    }

    private void RecalculateGlobalBusGuard()
    {
        TimeSpan? guard = null;
        foreach (var channel in _channels)
        {
            var requested = channel.Options.GlobalBusGuard;
            if (requested is null || requested.Value <= TimeSpan.Zero) continue;
            if (guard is null || requested > guard)
            {
                guard = requested;
            }
        }
        _globalBusGuard = guard;
    }

    public void Dispose() => _bus.Dispose();
    public IBusRTOptionsConfigurator Options => _bus.Options;
    public BusNativeHandle NativeHandle => _bus.NativeHandle;

    public void AddChannel(IsoTpChannelCore channel)
    {
        _channels.Add(channel);
        channel.OnWorkAvailable += OnWorkAvailable;
    }



    public void RemoveChannel(IsoTpChannelCore channel)
    {
        _channels.Remove(channel);
        channel.OnWorkAvailable -= OnWorkAvailable;
    }

}
