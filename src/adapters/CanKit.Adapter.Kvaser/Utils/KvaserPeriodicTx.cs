using System;
using System.Diagnostics;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Adapter.Kvaser.Native;

namespace CanKit.Adapter.Kvaser.Utils;

public sealed class KvaserPeriodicTx : IPeriodicTx
{
    private readonly KvaserBus _bus;
    private int _bufNo = -1;
    private CanFrame _frame;
    private bool _stopped;

    // State of the current run. CANlib does not report how many frames are left, so the
    // remaining count is derived from the time since the buffer was enabled.
    private int _runCount;          // frames programmed for the run: -1 infinite, 0 not running
    private TimeSpan _runPeriod;    // period as programmed into the buffer (whole microseconds)
    private TimeSpan _programmedPeriod;
    private long _runStarted;       // Stopwatch timestamp of the enable
    private bool _countProgrammed;  // a finite count may still be stored in the buffer

    private KvaserPeriodicTx(KvaserBus bus, int bufNo, CanFrame frame, PeriodicTxOptions options)
    {
        _bus = bus;
        _bufNo = bufNo;
        _frame = frame;

        Period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        RepeatCount = options.Repeat;
    }

    public static bool TryStart(KvaserBus bus, CanFrame frame, PeriodicTxOptions options, out KvaserPeriodicTx? periodicTx)
    {
        periodicTx = null;

        // Allocate object buffer of periodic TX type; index is returned as integer (negative => error)
        int bufNo = (int)Canlib.canObjBufAllocate(bus.Handle, (int)Canlib.canObjBufType.PERIODIC_TX);
        if (bufNo < 0)
        {
            CanKitLogger.LogDebug($"Kvaser: canObjBufAllocate failed: {(Canlib.canStatus)bufNo}");
            periodicTx = null;
            return false;
        }

        var tx = new KvaserPeriodicTx(bus, bufNo, frame, options);
        try
        {
            tx.ProgramBuffer(frame, tx.Period);

            // A finite Repeat sends Repeat frames in total (as the BCM and software schedulers do).
            // Repeat = 0 sends nothing at all.
            var bufferCount = options.IsInfinite ? -1 : options.Repeat;
            // Probe count support even for an infinite or idle job: Update() may later request
            // a finite count. Reject unsupported hardware before sending so fallback can apply.
            var initialCount = bufferCount > 0 ? (uint)bufferCount : 0u;
            var st = Canlib.canObjBufSetMsgCount(bus.Handle, bufNo, initialCount);
            if (st != Canlib.canStatus.canOK)
            {
                CanKitLogger.LogDebug($"Kvaser: canObjBufSetMsgCount failed: {st}");
                tx.Dispose();
                return false;
            }
            tx._countProgrammed = initialCount > 0;

            if (options.FireImmediately && bufferCount != 0)
            {
                // The immediate frame counts as the first one, but only if the driver accepted it.
                // Transmit returns 0 without throwing on a full TX buffer or a timeout.
                if (bus.Transmit([frame]) == 1 && bufferCount > 0)
                    bufferCount--;
            }

            tx.StartRun(bufferCount);

            bus.AttachOwner(tx);
            periodicTx = tx;
            return true;
        }
        catch
        {
            tx.Dispose();
            throw;
        }
    }

    public TimeSpan Period { get; private set; }
    public int RepeatCount { get; private set; }
    public int RemainingCount => throw new NotSupportedException("Kvaser hardware periodic RemainingCount is not supported.");

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        if (_bufNo >= 0)
        {
            try { _ = Canlib.canObjBufDisable(_bus.Handle, _bufNo); } catch { }
        }

    }

    public void Update(CanFrame? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        if (_stopped) throw new CanBusDisposedException();

        // Interrupt the run first. Frames sent while the buffer is being reprogrammed would
        // otherwise belong to the old run and be scheduled again by the new one.
        try { _ = Canlib.canObjBufDisable(_bus.Handle, _bufNo); } catch { }

        // Without a new count the frames that are still due stay due; the estimate is taken at
        // the point where the buffer stopped.
        var remaining = repeatCount ?? EstimateRemaining();

        if (frame is not null) _frame = frame.Value;
        if (period is not null) Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;
        if (repeatCount is not null) RepeatCount = repeatCount.Value;

        ProgramBuffer(_frame, Period);
        StartRun(remaining);
    }

    public event EventHandler? Completed
    {
        add => throw new NotSupportedException("Kvaser hardware periodic Completed event is not supported.");
        remove => throw new NotSupportedException("Kvaser hardware periodic Completed event is not supported.");
    }

    public void Dispose()
    {
        Stop();
        try
        {
            _ = Canlib.canObjBufFree(_bus.Handle, _bufNo);
        }
        catch
        {
        }
        finally
        {
            _bufNo = -1;
        }
    }

    /// <summary>
    /// Programs the message count for a run of <paramref name="count"/> frames (-1 = infinite)
    /// and enables the buffer. A count of 0 leaves the buffer disabled.
    /// </summary>
    private void StartRun(int count)
    {
        _runCount = 0;
        if (count == 0 || _bufNo < 0) return;

        if (count > 0)
        {
            // CANlib resets the count to 0 (infinite) once it is used up, so a finite count is
            // programmed before every enable instead of relying on what the buffer still holds.
            KvaserUtils.ThrowIfError(Canlib.canObjBufSetMsgCount(_bus.Handle, _bufNo, (uint)count),
                "canObjBufSetMsgCount", "Failed to set periodic message count");
            _countProgrammed = true;
        }
        else if (_countProgrammed)
        {
            // Switching to infinite while a finite count may not be used up yet: clear it,
            // otherwise the buffer would stop after the frames that were left.
            KvaserUtils.ThrowIfError(Canlib.canObjBufSetMsgCount(_bus.Handle, _bufNo, 0),
                "canObjBufSetMsgCount", "Failed to clear periodic message count");
            _countProgrammed = false;
        }

        StartBuffer();
        _runCount = count;
        _runPeriod = _programmedPeriod;
        _runStarted = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Frames still due in the current run: -1 for infinite, 0 when nothing is running.
    /// Derived from the elapsed time, so it can be off by one frame.
    /// </summary>
    private int EstimateRemaining()
    {
        if (_runCount <= 0) return _runCount;

        var elapsedTicks = (Stopwatch.GetTimestamp() - _runStarted) * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
        var sent = (long)(elapsedTicks / Math.Max(1, _runPeriod.Ticks));
        return (int)Math.Max(0, _runCount - sent);
    }

    private void StartBuffer()
    {
        if (_bufNo < 0) return;
        KvaserUtils.ThrowIfError(Canlib.canObjBufEnable(_bus.Handle, _bufNo), "canObjBufEnable", "Failed to enable periodic buffer");
    }

    private void ProgramBuffer(CanFrame frame, TimeSpan period)
    {
        if (_bufNo < 0) return;

        var us = (int)Math.Max(1, (long)Math.Round(period.TotalMilliseconds * 1000.0));
        KvaserUtils.ThrowIfError(Canlib.canObjBufSetPeriod(_bus.Handle, _bufNo, (uint)us), "canObjBufSetPeriod", "Failed to set period");
        // The buffer runs on the rounded value, so the remaining count is estimated with it.
        _programmedPeriod = TimeSpan.FromTicks((uint)us * (TimeSpan.TicksPerMillisecond / 1000));

        int id = (int)frame.ID;
        var data = frame.Data.ToArray();
        int dlc = data.Length;

        uint flags = 0;
        if (frame.FrameKind is CanFrameType.CanFd)
        {
            flags |= Canlib.canFDMSG_FDF;
            if (frame.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
            if (frame.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
            if (frame.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (_bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) flags |= Canlib.canMSG_SINGLE_SHOT;
        }
        else if (frame.FrameKind is CanFrameType.Can20)
        {
            if (frame.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (frame.IsRemoteFrame) flags |= Canlib.canMSG_RTR;
            if (_bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) flags |= Canlib.canMSG_SINGLE_SHOT;
        }

        KvaserUtils.ThrowIfError(Canlib.canObjBufWrite(_bus.Handle, _bufNo, id, data, (uint)dlc, flags),
            "canObjBufWrite", "Failed to write periodic frame");
    }
}
