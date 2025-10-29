using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserClassicTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        var ch = (KvaserBus)bus;
        int sent = 0;
        foreach (var item in frames)
        {
            if (item is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("Kvaser classic transceiver requires CanClassicFrame.");
            }

            var flags = 0u;
            flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
            if (cf.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (cf.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

            Canlib.canStatus st;
            unsafe
            {
                fixed (byte* ptr = item.Data.Span)
                {
                    st = Canlib.canWrite(ch.Handle, item.ID, ptr, (uint)cf.Data.Length, flags);
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

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<ICanFrame> frames)
    {
        var ch = (KvaserBus)bus;
        int sent = 0;
        foreach (var item in frames)
        {
            if (item is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("Kvaser classic transceiver requires CanClassicFrame.");
            }

            var flags = 0u;
            flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
            if (cf.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (cf.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

            Canlib.canStatus st;
            unsafe
            {
                fixed (byte* ptr = item.Data.Span)
                {
                    st = Canlib.canWrite(ch.Handle, item.ID, ptr, (uint)cf.Data.Length, flags);
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

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in ICanFrame frame)
    {
        var ch = (KvaserBus)bus;
        if (frame is not CanClassicFrame cf)
        {
            throw new InvalidOperationException("Kvaser classic transceiver requires CanClassicFrame.");
        }
        var flags = 0u;
        flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
        if (cf.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
        if (cf.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

        Canlib.canStatus st;
        unsafe
        {
            fixed (byte* ptr = cf.Data.Span)
            {
                st = Canlib.canWrite(ch.Handle, cf.ID, ptr, (uint)cf.Data.Length, flags);
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
        var data = new byte[8];
        while (true)
        {
            KvaserUtils.Clear(data, 8);
            int id;
            int flags;
            uint time;
            int dlc;
            var st = Canlib.canRead(ch.Handle, out id, data, out dlc, out flags, out time);

            if (st == Canlib.canStatus.canOK)
            {
                var isExt = (flags & Canlib.canMSG_EXT) != 0;
                var isRtr = (flags & Canlib.canMSG_RTR) != 0;
                var isErr = (flags & Canlib.canMSG_ERROR_FRAME) != 0;
                var payLoad = bus.Options.BufferAllocator.Rent(dlc);
                data.AsSpan().Slice(0, dlc).CopyTo(payLoad.Memory.Span);
                var frame = new CanClassicFrame(id, payLoad, isExt, isRtr,
                        bus.Options.BufferAllocator.FrameNeedDispose)
                { IsErrorFrame = isErr };
                var kch = (KvaserBus)bus;
                var ticks = time * kch.Options.TimerScaleMicroseconds * 10L; // us -> ticks
                yield return new CanReceiveData(frame)
                {
                    ReceiveTimestamp = TimeSpan.FromTicks(ticks),
                    IsEcho = (flags & Canlib.canMSG_TXACK) != 0,
                };
            }
            else if (st == Canlib.canStatus.canERR_NOMSG)
            {
                break;
            }
            else
            {
                var msg = "Failed to read frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canRead", msg);
                break;
            }
        }
        yield break;
    }
}
