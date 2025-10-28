using System;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Adapter.Kvaser.Native;

namespace CanKit.Adapter.Kvaser.Utils;

public sealed class KvaserPeriodicTx : IPeriodicTx
{
    private readonly KvaserBus _bus;
    private int _bufNo = -1;
    private ICanFrame _frame;
    private bool _stopped;

    private KvaserPeriodicTx(KvaserBus bus, int bufNo, ICanFrame frame, PeriodicTxOptions options)
    {
        _bus = bus;
        _bufNo = bufNo;
        _frame = frame;

        Period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        RepeatCount = options.Repeat;

        if (options.FireImmediately)
        {
            _ = _bus.Transmit([frame]);
        }

        ProgramBuffer(_frame, Period);
        StartBuffer();
    }

    public static bool TryStart(KvaserBus bus, ICanFrame frame, PeriodicTxOptions options, out KvaserPeriodicTx? periodicTx)
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

        try
        {
            periodicTx = new KvaserPeriodicTx(bus, bufNo, frame, options);
            bus.AttachOwner(periodicTx);
            return true;
        }
        catch
        {
            try { _ = Canlib.canObjBufFree(bus.Handle, bufNo); } catch { }
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

    public void Update(ICanFrame? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        if (_stopped) throw new CanBusDisposedException();

        if (frame is not null) _frame = frame;
        if (period is not null) Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;
        if (repeatCount is not null) RepeatCount = repeatCount.Value;

        ProgramBuffer(_frame, Period);
        try { _ = Canlib.canObjBufDisable(_bus.Handle, _bufNo); } catch { }
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

    private void ProgramBuffer(ICanFrame frame, TimeSpan period)
    {
        if (_bufNo < 0) return;

        var us = (int)Math.Max(1, (long)Math.Round(period.TotalMilliseconds * 1000.0));
        KvaserUtils.ThrowIfError(Canlib.canObjBufSetPeriod(_bus.Handle, _bufNo, (uint)us), "canObjBufSetPeriod", "Failed to set period");

        int id = (int)frame.ID;
        var data = frame.Data.ToArray();
        int dlc = data.Length;

        uint flags = 0;
        if (frame is CanFdFrame fd)
        {
            flags |= Canlib.canFDMSG_FDF;
            if (fd.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
            if (fd.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
            if (fd.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (_bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) flags |= Canlib.canMSG_SINGLE_SHOT;
        }
        else if (frame is CanClassicFrame classic)
        {
            if (classic.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (classic.IsRemoteFrame) flags |= Canlib.canMSG_RTR;
            if (_bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) flags |= Canlib.canMSG_SINGLE_SHOT;
        }

        KvaserUtils.ThrowIfError(Canlib.canObjBufWrite(_bus.Handle, _bufNo, id, data, (uint)dlc, flags),
            "canObjBufWrite", "Failed to write periodic frame");
    }
}
