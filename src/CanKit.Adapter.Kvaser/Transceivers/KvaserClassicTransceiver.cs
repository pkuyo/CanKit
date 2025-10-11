using System.Collections.Generic;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserClassicTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        int sent = 0;
        var startTime = Environment.TickCount;
        foreach (var item in frames)
        {
            if (item is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("Kvaser classic transceiver requires CanClassicFrame.");
            }
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;
            var flags = 0u;
            flags |= (channel.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
            if (cf.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
            if (cf.IsRemoteFrame) flags |= Canlib.canMSG_RTR;

            var data = cf.Data.ToArray();
            var dlc = data.Length;
            Canlib.canStatus st;
            unsafe
            {
                fixed (byte* ptr = item.Data.Span)
                {
                    st = timeOut switch
                    {
                        0 => CanlibNative.canWrite(ch.Handle, (int)item.ID, ptr, (uint)dlc, flags),
                        > 0 => CanlibNative.canWriteWait(ch.Handle, (int)item.ID, ptr, (uint)dlc, flags, (uint)remainingTime),
                        < 0 => CanlibNative.canWriteWait(ch.Handle, (int)item.ID, ptr, (uint)dlc, flags, uint.MaxValue),
                    };
                }
            }
            if (st == Canlib.canStatus.canOK)
            {
                sent++;
            }
            else if (st == Canlib.canStatus.canERR_TXBUFOFL)
            {
                break;
            }
            else
            {
                var msg = "Failed to write frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(
                    st, timeOut != 0 ? "canWriteWait" : "canWrite", msg);
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        var ch = (KvaserBus)bus;
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var startTime = Environment.TickCount;
        while (true)
        {
            var data = new byte[8];
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;
            int id;
            int flags;
            long time;
            int recCount = 0;
            var st = timeOut switch
            {
                0 => Canlib.canRead(ch.Handle, out id, data, out _, out flags, out time),
                > 0 => Canlib.canReadWait(ch.Handle, out id, data, out _, out flags, out time, remainingTime),
                < 0 => Canlib.canReadWait(ch.Handle, out id, data, out _, out flags, out time, long.MaxValue),
            };

            if (st == Canlib.canStatus.canOK)
            {
                var isExt = (flags & Canlib.canMSG_EXT) != 0;
                var isRtr = (flags & Canlib.canMSG_RTR) != 0;
                var isErr = (flags & Canlib.canMSG_ERROR_FRAME) != 0;
                var buf = data;
                var frame = new CanClassicFrame(id, buf, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };
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
