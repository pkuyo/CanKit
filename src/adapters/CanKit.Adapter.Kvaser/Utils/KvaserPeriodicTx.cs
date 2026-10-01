using System;
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

            // A finite Repeat sends Repeat frames in total (as the BCM and software schedulers do);
            // the immediate frame counts as the first one. Repeat = 0 sends nothing at all.
            var bufferCount = options.IsInfinite ? -1 : options.Repeat - (options.FireImmediately ? 1 : 0);
            if (bufferCount > 0)
            {
                // Program the count before anything is sent, so a device without message count
                // support falls back cleanly instead of sending forever.
                var st = Canlib.canObjBufSetMsgCount(bus.Handle, bufNo, (uint)bufferCount);
                if (st != Canlib.canStatus.canOK)
                {
                    CanKitLogger.LogDebug($"Kvaser: canObjBufSetMsgCount failed: {st}");
                    tx.Dispose();
                    return false;
                }
            }

            if (options.FireImmediately && (options.IsInfinite || options.Repeat > 0))
            {
                _ = bus.Transmit([frame]);
            }

            if (options.IsInfinite || bufferCount > 0)
            {
                tx.StartBuffer();
            }

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

        if (frame is not null) _frame = frame.Value;
        if (period is not null) Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;
        if (repeatCount is not null) RepeatCount = repeatCount.Value;

        ProgramBuffer(_frame, Period);
        try { _ = Canlib.canObjBufDisable(_bus.Handle, _bufNo); } catch { }

        // CANlib resets the message count to 0 (infinite) once it is used up, so a finite
        // RepeatCount has to be programmed again before every enable.
        if (RepeatCount == 0) return;
        if (RepeatCount > 0)
        {
            KvaserUtils.ThrowIfError(Canlib.canObjBufSetMsgCount(_bus.Handle, _bufNo, (uint)RepeatCount),
                "canObjBufSetMsgCount", "Failed to set periodic message count");
        }
        StartBuffer();
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
