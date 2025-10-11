using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Adapter.ZLG.Diagnostics;
using CanKit.Adapter.ZLG.Native;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ZLG.Utils;

public sealed class ZlgPeriodicTx : IPeriodicTx
{
    private readonly ZlgCanBus _bus;
    private readonly object _evtGate = new();
    private ICanFrame _frame;
    private ushort _index;
    private volatile int _remaining;
    private bool _stopped;

    public ZlgPeriodicTx(ZlgCanBus bus, ICanFrame frame, PeriodicTxOptions options)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));


        if (options.Repeat >= 0)
        {
            throw new CanBusConfigurationException(
                "ZLG periodic transmit does not support finite Repeat values; use Repeat = -1.");
        }

        // Validate frame type vs channel
        if (frame is CanClassicFrame && bus.Options.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanClassic, bus.Options.Features);
        if (frame is CanFdFrame && bus.Options.ProtocolMode != CanProtocolMode.CanFd)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, bus.Options.Features);

        _frame = frame;
        Period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        RepeatCount = options.Repeat;
        _remaining = options.Repeat;

        // Unique index (best effort, ushort range)
        _index = (ushort)bus.GetAutoSendIndex();

        // Fire once immediately if requested
        if (options.FireImmediately)
        {
            try
            {
                _ = _bus.Transmit(new[] { frame }, 0);
            }
            catch
            {
            }

            if (_remaining >= 0) _remaining = Math.Max(0, _remaining - 1);
        }

        // Program device auto transmit; repeat is managed by software monitor if finite
        ApplyHardware(true, _frame, Period);
    }

    public TimeSpan Period { get; private set; }
    public int RepeatCount { get; private set; }
    public int RemainingCount => _remaining;

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        try
        {
            StopHardware();
            _bus.FreeAutoSendIndex(_index);
        }
        catch
        {
        }
    }

    public void Update(ICanFrame? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        if (_stopped) throw new CanBusDisposedException();
        if (frame is not null) _frame = frame;
        if (period is not null) Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;
        if (repeatCount is not null)
        {
            RepeatCount = repeatCount.Value;
            _remaining = repeatCount.Value;
        }

        ApplyHardware(true, _frame, Period);
    }

    public void Dispose() => Stop();

    public event EventHandler? Completed
    {
        add
        {
            throw new CanKit.Core.Exceptions.CanKitException(
                CanKit.Core.Exceptions.CanKitErrorCode.FeatureNotSupported,
                "ZLG periodic transmit does not support Completed event notifications.");
        }
        remove
        {
            throw new CanKit.Core.Exceptions.CanKitException(
                CanKit.Core.Exceptions.CanKitErrorCode.FeatureNotSupported,
                "ZLG periodic transmit does not support Completed event notifications.");
        }
    }

    private void StopHardware()
    {
        // Disable only this auto-send entry (by index) instead of clearing all
        // entries on the channel, then apply the change.
        ApplyHardware(false, _frame, Period);
    }

    private unsafe void ApplyHardware(bool enable, ICanFrame frame, TimeSpan period)
    {
        var interval = (uint)Math.Max(1, (int)Math.Round(period.TotalMilliseconds));
        var chan = _bus.Options.ChannelIndex;

        if (frame is CanClassicFrame classic)
        {
            var obj = new ZLGCAN.ZCAN_AUTO_TRANSMIT_OBJ
            {
                enable = (ushort)(enable ? 1 : 0),
                index = _index,
                interval = interval,
                obj = classic.ToTransmitData(_bus.Options.WorkMode == ChannelWorkMode.Echo)
            };

            var path = $"{chan}/auto_send";
            var ret = ZLGCAN.ZCAN_SetValue(_bus.NativeHandle.DeviceHandle, path, (IntPtr)(&obj));
            ZlgErr.ThrowIfError(ret, "ZCAN_SetValue(auto_send)", _bus.NativeHandle);
        }
        else if (frame is CanFdFrame fd)
        {
            var obj = new ZLGCAN.ZCANFD_AUTO_TRANSMIT_OBJ
            {
                enable = (ushort)(enable ? 1 : 0),
                index = _index,
                interval = interval,
                obj = fd.ToTransmitData(_bus.Options.WorkMode == ChannelWorkMode.Echo)
            };

            var path = $"{chan}/auto_send_canfd";
            var ret = ZLGCAN.ZCAN_SetValue(_bus.NativeHandle.DeviceHandle, path, (IntPtr)(&obj));
            ZlgErr.ThrowIfError(ret, "ZCAN_SetValue(auto_send_canfd)", _bus.NativeHandle);
        }
        else
        {
            throw new NotSupportedException("Unsupported frame type for ZLG periodic transmit.");
        }


        ZlgErr.ThrowIfError(ZLGCAN.ZCAN_SetValue(_bus.NativeHandle.DeviceHandle, $"{chan}/apply_auto_send", "0"),
            "ZCAN_SetValue(apply_auto_send)", _bus.NativeHandle);
    }
}
