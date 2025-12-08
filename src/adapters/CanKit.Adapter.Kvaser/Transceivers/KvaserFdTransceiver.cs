using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserFdTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<CanFrame> frames)
    {
        var ch = (KvaserBus)bus;
        int sent = 0;

        foreach (var item in frames)
        {
            var dlc = item.Data.Length;
            var flags = item.FrameKind is CanFrameType.CanFd ? Canlib.canFDMSG_FDF : 0u;
            flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
            if (item.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (item.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
            if (item.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
            if (item.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

            Canlib.canStatus st;
            unsafe
            {
                fixed (byte* ptr = item.Data.Span)
                {
                    st = Canlib.canWrite(ch.Handle, item.ID, ptr, (uint)dlc, flags);
                }
            }

            if (st == Canlib.canStatus.canOK)
            {
                sent++;
            }
            else if (st is Canlib.canStatus.canERR_TXBUFOFL or Canlib.canStatus.canERR_TIMEOUT)
            {
                return sent;
            }
            else
            {
                var msg = "Failed to write frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canWrite", msg);
            }
        }

        return sent;
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<CanFrame> frames)
    {
        var ch = (KvaserBus)bus;
        int sent = 0;

        foreach (var item in frames)
        {
            var dlc = item.Data.Length;
            var flags = item.FrameKind is CanFrameType.CanFd ? Canlib.canFDMSG_FDF : 0u;
            flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
            if (item.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (item.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
            if (item.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
            if (item.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

            Canlib.canStatus st;
            unsafe
            {
                fixed (byte* ptr = item.Data.Span)
                {
                    st = Canlib.canWrite(ch.Handle, item.ID, ptr, (uint)dlc, flags);
                }
            }

            if (st == Canlib.canStatus.canOK)
            {
                sent++;
            }
            else if (st is Canlib.canStatus.canERR_TXBUFOFL or Canlib.canStatus.canERR_TIMEOUT)
            {
                return sent;
            }
            else
            {
                var msg = "Failed to write frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canWrite", msg);
            }
        }

        return sent;
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frame)
    {
        var ch = (KvaserBus)bus;
        var dlc = frame.Data.Length;
        var flags = frame.FrameKind is CanFrameType.CanFd ? Canlib.canFDMSG_FDF : 0u;
        flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
        if (frame.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
        if (frame.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
        if (frame.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
        if (frame.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

        Canlib.canStatus st;
        unsafe
        {
            fixed (byte* ptr = frame.Data.Span)
            {
                st = Canlib.canWrite(ch.Handle, frame.ID, ptr, (uint)dlc, flags);
            }
        }

        if (st == Canlib.canStatus.canOK)
        {
            return 1;
        }
        if (st is Canlib.canStatus.canERR_TXBUFOFL or Canlib.canStatus.canERR_TIMEOUT)
        {
            return 0;
        }
        var msg = "Failed to write frame";
        if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
            msg += $". Message:{str}";
        KvaserUtils.ThrowIfError(st, "canWrite", msg);
        return 0;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int _ = 0)
    {
        var ch = (KvaserBus)bus;
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        int recCount = 0;
        var data = new byte[64];
        while (true)
        {
            int id;
            int flags;
            uint time;
            int dlc;
            var st = Canlib.canRead(ch.Handle, out id, data, out dlc, out flags, out time);
            if (st == Canlib.canStatus.canOK)
            {
                dlc = Math.Min(dlc, 64);
                var isFd = (flags & Canlib.canFDMSG_FDF) != 0;
                var isExt = (flags & Canlib.canMSG_EXT) != 0;
                var brs = (flags & Canlib.canFDMSG_BRS) != 0;
                var esi = (flags & Canlib.canFDMSG_ESI) != 0;
                var isErr = (flags & Canlib.canMSG_ERROR_FRAME) != 0;
                var isRtr = (flags & Canlib.canMSG_RTR) != 0;
                var payLoad = bus.Options.BufferAllocator.Rent(dlc);
                data.AsSpan().Slice(0, dlc).CopyTo(payLoad.Memory.Span);
                CanFrame frame = isFd
                    ? CanFrame.Fd(id, payLoad, brs, esi, isExt,
                        bus.Options.BufferAllocator.FrameNeedDispose, isErr)
                    : CanFrame.Classic(id, payLoad, isExt, isRtr,
                        bus.Options.BufferAllocator.FrameNeedDispose, isErr);

                var kch = (KvaserBus)bus;
                var ticks = time * kch.Options.TimerScaleMicroseconds * 10L; // us -> ticks
                yield return new CanReceiveData(frame)
                {
                    ReceiveTimestamp = TimeSpan.FromTicks(ticks),
                    IsEcho = (flags & Canlib.canMSG_TXACK) != 0,
                };

                recCount++;
                if (recCount == count)
                    yield break;
            }
            else if (st == Canlib.canStatus.canERR_NOMSG)
            {
                yield break;
            }
            else
            {
                var msg = "Failed to read frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canRead", msg);
                yield break;
            }
        }

    }
}
