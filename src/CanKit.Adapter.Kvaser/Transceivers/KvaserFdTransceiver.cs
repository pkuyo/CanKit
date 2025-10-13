using System.Collections.Generic;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserFdTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        int sent = 0;

        if (timeOut == 0)
        {
            foreach (var item in frames)
            {
                var dlc = item.Data.Length;
                var flags = 0U;
                if (item is CanFdFrame fd)
                {
                    flags = Canlib.canFDMSG_FDF;
                    flags |= (channel.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
                    if (fd.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                    if (fd.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
                    if (fd.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
                }
                else if (item is CanClassicFrame classic)
                {
                    if (classic.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                    if (classic.IsRemoteFrame) flags |= Canlib.canMSG_RTR;
                }

                Canlib.canStatus st;
                unsafe
                {
                    fixed (byte* ptr = item.Data.Span)
                    {
                        st = CanlibNative.canWrite(ch.Handle, (int)item.ID, ptr, (uint)dlc, flags);
                    }
                }

                if (st == Canlib.canStatus.canOK)
                {
                    sent++;
                }
                else if (st == Canlib.canStatus.canERR_TXBUFOFL)
                {
                    return -sent;
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

        var startTime = Environment.TickCount;
        using var e = frames.GetEnumerator();
        bool hasMore = e.MoveNext();

        while (hasMore)
        {
            while (hasMore)
            {
                var item = e.Current;
                var dlc = item.Data.Length;
                var flags = 0U;
                if (item is CanFdFrame fd)
                {
                    flags = Canlib.canFDMSG_FDF;
                    flags |= (channel.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
                    if (fd.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                    if (fd.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
                    if (fd.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
                }
                else if (item is CanClassicFrame classic)
                {
                    if (classic.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                    if (classic.IsRemoteFrame) flags |= Canlib.canMSG_RTR;
                }

                Canlib.canStatus st;
                unsafe
                {
                    fixed (byte* ptr = item.Data.Span)
                    {
                        st = CanlibNative.canWrite(ch.Handle, (int)item.ID, ptr, (uint)dlc, flags);
                    }
                }

                if (st == Canlib.canStatus.canOK)
                {
                    sent++;
                    hasMore = e.MoveNext();
                    continue;
                }
                if (st == Canlib.canStatus.canERR_TXBUFOFL)
                {
                    break;
                }

                var msg = "Failed to write frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canWrite", msg);
            }

            if (!hasMore)
                break;

            uint remaining;
            if (timeOut > 0)
            {
                remaining = (uint)Math.Max(0, timeOut - (Environment.TickCount - startTime));
                if (remaining == 0)
                    break;
            }
            else
            {
                remaining = uint.MaxValue;
            }

            var stSync = CanlibNative.canWriteSync(ch.Handle, (uint)remaining);
            if (stSync != Canlib.canStatus.canOK && stSync != Canlib.canStatus.canERR_TIMEOUT)
            {
                var msg = "Failed to wait for write sync";
                if (Canlib.canGetErrorText(stSync, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(stSync, "canWriteSync", msg);
            }
        }

        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        var ch = (KvaserBus)bus;
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var startTime = Environment.TickCount;
        var recCount = 0;
        while (true)
        {
            byte[] data = new byte[64];
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;
            int id;
            int flags;
            long time;
            int dlc;
            var st = timeOut switch
            {
                0 => Canlib.canRead(ch.Handle, out id, data, out dlc, out flags, out time),
                > 0 => Canlib.canReadWait(ch.Handle, out id, data, out dlc, out flags, out time, remainingTime),
                < 0 => Canlib.canReadWait(ch.Handle, out id, data, out dlc, out flags, out time, long.MaxValue),
            };

            if (st == Canlib.canStatus.canOK)
            {
                var isFd = (flags & Canlib.canFDMSG_FDF) != 0;

                var isExt = (flags & Canlib.canMSG_EXT) != 0;
                var brs = (flags & Canlib.canFDMSG_BRS) != 0;
                var esi = (flags & Canlib.canFDMSG_ESI) != 0;
                var isErr = (flags & Canlib.canMSG_ERROR_FRAME) != 0;
                var isRtr = (flags & Canlib.canMSG_RTR) != 0;

                ICanFrame frame;
                if (isFd)
                {
                    frame = new CanFdFrame(id, new ArraySegment<byte>(data, 0, CanFdFrame.DlcToLen((byte)dlc)), brs, esi)
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr };
                }
                else
                {
                    frame = new CanClassicFrame(id, new ArraySegment<byte>(data, 0, dlc))
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr, IsRemoteFrame = isRtr };
                }

                // Convert using configured timer_scale (microseconds per unit)
                var kch = (KvaserBus)bus;
                var ticks = time * kch.Options.TimerScaleMicroseconds * 10L; // us -> ticks
                yield return new CanReceiveData(frame) { ReceiveTimestamp = TimeSpan.FromTicks(ticks) };

                recCount++;
                if (recCount == count)
                {
                    break;
                }
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
                KvaserUtils.ThrowIfError(
                    st,
                    timeOut != 0 ? "canReadWait" : "canRead", msg);
                break;
            }
        }
    }
}
