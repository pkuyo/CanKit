using System.Collections.Generic;
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
        foreach (var item in frames)
        {
            if (item.CanFrame is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("Kvaser classic transceiver requires CanClassicFrame.");
            }
            var flags = 0u;
            if (cf.IsExtendedFrame) flags |= (uint)Canlib.canMSG_EXT;
            if (cf.IsRemoteFrame) flags |= (uint)Canlib.canMSG_RTR;

            var data = cf.Data.ToArray();
            var dlc = (int)Math.Min(8, data.Length);
            if (data.Length < dlc)
            {
                Array.Resize(ref data, dlc);
            }

            var st = Canlib.canWrite(ch.Handle, (int)cf.ID, data, dlc, (int)flags);
            if (st == Canlib.canStatus.canOK)
            {
                sent++;
            }
            else
            {
                //TODO:异常处理
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        var list = new List<CanReceiveData>();
        var timeout = timeOut < 0 ? -1 : timeOut;
        for (int i = 0; i < count; i++)
        {
            var data = new byte[8];
            var st = timeout > 0
                ? Canlib.canReadWait(ch.Handle, out var id, data, out var dlc, out var flags, out var time, timeout)
                : Canlib.canRead(ch.Handle, out id, data, out dlc, out flags, out time);

            if (st == Canlib.canStatus.canOK)
            {
                var isExt = (flags & (int)Canlib.canMSG_EXT) != 0;
                var isRtr = (flags & (int)Canlib.canMSG_RTR) != 0;
                var isErr = (flags & (int)Canlib.canMSG_ERROR_FRAME) != 0;
                var buf = data;
                if (buf.Length > dlc)
                {
                    Array.Resize(ref buf, dlc);
                }
                var frame = new CanClassicFrame((uint)id, buf, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };
                // Kvaser timestamp is in milliseconds from start of driver; convert to ticks (100ns)
                var ticks = (ulong)time * 10_000UL;
                list.Add(new CanReceiveData(frame) { recvTimestamp = ticks });
            }
            else if (st == Canlib.canStatus.canERR_NOMSG)
            {
                break;
            }
            else
            {
                //TODO:异常处理
                break;
            }
        }
        return list;
    }
}
