using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ControlCAN.Diagnostics;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN.Utils;

public sealed class ControlCanPeriodicTx : IPeriodicTx
{
    private ControlCanBus _bus;
    private readonly object _evtGate = new();
    private CanFrame _frame;
    private ushort _index;
    private volatile int _remaining;
    private bool _stopped;
    private bool _retry;

    public ControlCanPeriodicTx(ControlCanBus bus, CanFrame frame, PeriodicTxOptions options)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));


        if (options.Repeat >= 0)
        {
            throw new CanBusConfigurationException(
                "ControlCAN periodic transmit does not support finite Repeat values; use Repeat = -1.");
        }

        // Validate frame type vs channel
        if (frame.FrameKind is not CanFrameType.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, bus.Options.Features);

        _frame = frame;
        Period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        RepeatCount = options.Repeat;
        _remaining = options.Repeat;
        _retry = bus.Options.TxRetryPolicy == TxRetryPolicy.AlwaysRetry;
        // Unique index (best effort, ushort range)
        _index = (ushort)bus.GetAutoSendIndex();

        // Fire once immediately if requested
        if (options.FireImmediately)
        {
            try
            {
                _ = _bus.Transmit(frame);
            }
            catch
            {
            }
        }

        // Program device auto transmit; repeat is managed by software monitor if finite
        ApplyHardware(true, _frame, Period);
    }


    public void Dispose()
    {
        Stop();
        _bus.FreeAutoSendIndex(_index);
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        try
        {
            StopHardware();
        }
        catch
        {/*ignored*/}
    }

    private unsafe void ApplyHardware(bool enable, CanFrame frame, TimeSpan period)
    {
        var pObj = stackalloc CcApi.VCI_AUTO_SEND_OBJ[1];
        pObj->Enable = (byte)(enable ? 1U : 0U);
        pObj->Index = (byte)_index;
        pObj->Interval = (uint)(period.TotalMilliseconds*2);
        frame.ToNative(&pObj->Obj, _retry);
        ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(_bus.RawDevType, _bus.DevIndex, _bus.CanIndex, 5, pObj),
            "VCI_SetReference(period)", _bus,
            $"failed to {(enable ? "enable" : "disable")} filter. FilterIndex={_index}");
    }

    private void StopHardware()
    {
        // Disable only this auto-send entry (by index) instead of clearing all
        // entries on the channel, then apply the change.
        ApplyHardware(false, _frame, Period);
    }
    public void Update(CanFrame? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        if (_stopped) throw new CanBusDisposedException();
        if (frame is not null)
        {
            if (frame.Value.FrameKind is not CanFrameType.Can20)
            {
                throw new CanFeatureNotSupportedException(CanFeature.CanFd, _bus.Options.Features);
            }
            _frame = frame.Value;
        }
        if (period is not null) Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;
        if (repeatCount is not null)
        {
            RepeatCount = repeatCount.Value;
            _remaining = repeatCount.Value;
        }

        ApplyHardware(true, _frame, Period);
    }
    public TimeSpan Period { get; private set; }
    public int RepeatCount { get; private set; }
    public int RemainingCount { get; } = -1;
    public event EventHandler? Completed
    {
        add
        {
            throw new CanKitException(
                CanKitErrorCode.FeatureNotSupported,
                "ControlCAN periodic transmit does not support Completed event notifications.");
        }
        remove
        {
            throw new CanKitException(
                CanKitErrorCode.FeatureNotSupported,
                "ControlCAN periodic transmit does not support Completed event notifications.");
        }
    }
}
