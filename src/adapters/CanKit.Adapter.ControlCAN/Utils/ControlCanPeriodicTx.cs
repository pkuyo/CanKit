using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN.Utils;

public sealed class ControlCanPeriodicTx : IPeriodicTx
{
    private ControlCanBus _bus;
    private readonly object _lifecycleGate = new();
    private CanFrame _frame;
    private ushort _index;
    private volatile int _remaining;
    private bool _stopped;
    private bool _disposed;
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
        try
        {
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
        catch
        {
            // A constructor that throws is never Disposed by the caller: return the
            // auto-send slot already taken so failed attempts cannot exhaust the pool.
            try { _bus.FreeAutoSendIndex(_index); } catch { /* ignored */ }
            throw;
        }
    }


    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            Stop();
            if (!_stopped) return; // A failed disable remains retryable, with its slot reserved.
            _disposed = true;
            _bus.FreeAutoSendIndex(_index);
        }
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            if (_stopped) return;
            try
            {
                StopHardware();
                _stopped = true;
            }
            catch (CanBusDisposedException)
            {
                // Bus cleanup owns any remaining hardware entry once shutdown starts.
                _stopped = true;
            }
            catch (Exception ex)
            {
                // Preserve the non-throwing Stop/Dispose contract, but allow a retry.
                try { CanKitLogger.LogWarning($"ControlCAN: Failed to stop auto-send entry {_index}; Stop or Dispose can retry.", ex); }
                catch { /* Logging must not prevent a later retry. */ }
            }
        }
    }

    private unsafe void ApplyHardware(bool enable, CanFrame frame, TimeSpan period)
    {
        var pObj = stackalloc CcApi.VCI_AUTO_SEND_OBJ[1];
        pObj->Enable = (byte)(enable ? 1U : 0U);
        pObj->Index = (byte)_index;
        pObj->Interval = (uint)(period.TotalMilliseconds*2);
        frame.ToNative(&pObj->Obj, _retry);
        _bus.ApplyAutoSend(*pObj);
    }

    private void StopHardware()
    {
        // Disable only this auto-send entry (by index) instead of clearing all
        // entries on the channel, then apply the change.
        ApplyHardware(false, _frame, Period);
    }
    public void Update(CanFrame? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        lock (_lifecycleGate)
        {
            UpdateHardware(frame, period, repeatCount);
        }
    }

    private void UpdateHardware(CanFrame? frame, TimeSpan? period, int? repeatCount)
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
