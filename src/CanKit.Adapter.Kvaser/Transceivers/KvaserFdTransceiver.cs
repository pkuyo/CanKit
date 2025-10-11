using System.Collections.Generic;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserFdTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        uint sent = 0;
        var startTime = Environment.TickCount;
        foreach (var item in frames)
        {
            var data = item.Data.ToArray();
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;
            //Canlib.canWrite use data length as DLC
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

            var st = timeOut switch
            {
                0 => Canlib.canWrite(ch.Handle, (int)item.ID, data, dlc, (int)flags),
                > 0 => Canlib.canWriteWait(ch.Handle, (int)item.ID, data, dlc, (int)flags, remainingTime),
                < 0 => Canlib.canWriteWait(ch.Handle, (int)item.ID, data, dlc, (int)flags, long.MaxValue),
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
        var list = new List<CanReceiveData>();
        var startTime = Environment.TickCount;
        for (int i = 0; i < count; i++)
        {
            byte[] data = new byte[64];
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
                var isFd = (flags & Canlib.canFDMSG_FDF) != 0;

                var isExt = (flags & Canlib.canMSG_EXT) != 0;
                var brs = (flags & Canlib.canFDMSG_BRS) != 0;
                var esi = (flags & Canlib.canFDMSG_ESI) != 0;
                var isErr = (flags & Canlib.canMSG_ERROR_FRAME) != 0;
                var isRtr = (flags & Canlib.canMSG_RTR) != 0;

                ICanFrame frame;
                if (isFd)
                {
                    frame = new CanFdFrame((uint)id, data, brs, esi)
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr };
                }
                else
                {
                    frame = new CanClassicFrame((uint)id, data)
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr, IsRemoteFrame = isRtr };
                }

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
