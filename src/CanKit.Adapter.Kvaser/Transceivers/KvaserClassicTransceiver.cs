using System.Collections.Generic;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        uint sent = 0;
        var startTime = Environment.TickCount;
        foreach (var item in frames)
        {
            if (item.CanFrame is not CanClassicFrame cf)
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
            var st = timeOut switch
            {
                0 => Canlib.canWrite(ch.Handle, (int)cf.ID, data, dlc, (int)flags),
                > 0 => Canlib.canWriteWait(ch.Handle, (int)cf.ID, data, dlc, (int)flags, remainingTime),
                < 0 => Canlib.canWriteWait(ch.Handle, (int)cf.ID, data, dlc, (int)flags, long.MaxValue),
            };
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

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int timeOut = 0)
    {
        var ch = (KvaserBus)bus;
        var list = new List<CanReceiveData>((int)count);
        var startTime = Environment.TickCount;
        for (int i = 0; i < count; i++)
        {
            var data = new byte[8];
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;
            int id;
            int flags;
            long time;
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
                var frame = new CanClassicFrame((uint)id, buf, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };
                // Convert using configured timer_scale (microseconds per unit)
                var kch = (KvaserBus)bus;
                var ticks = time * kch.Options.TimerScaleMicroseconds * 10L; // us -> ticks
                list.Add(new CanReceiveData(frame) { ReceiveTimestamp = TimeSpan.FromTicks(ticks) });

                if (list.Count == count)
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
        return list;
    }
}
