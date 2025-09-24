using Peak.Can.Basic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.PCAN.Transceivers;

public sealed class PcanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)channel;
        uint sent = 0;
        foreach (var item in frames)
        {
            if (item.CanFrame is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("PCAN classic transceiver requires CanClassicFrame.");
            }

            var type = MessageType.Standard;
            if (cf.IsExtendedFrame)
            {
                type |= MessageType.Extended;
            }

            if (cf.IsRemoteFrame)
            {
                type |= MessageType.RemoteRequest;
            }

            var dlc = (byte)Math.Min(8, (int)cf.Dlc);
            byte[] payload;
            if ((type & MessageType.RemoteRequest) != 0)
            {
                payload = [];
            }
            else
            {
                payload = cf.Data.ToArray();
                if (payload.Length > dlc)
                    Array.Resize(ref payload, dlc);
            }

            var msg = new PcanMessage(cf.ID, type, dlc, payload, extendedDataLength: false);

            var st = Api.Write(ch.Handle, msg);
            if (st == PcanStatus.OK)
            {
                sent++;
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)channel;
        var list = new List<CanReceiveData>();
        for (int i = 0; i < count; i++)
        {
            PcanMessage pmsg;
            ulong ts;
            var st = Api.Read(ch.Handle, out pmsg, out ts);
            if (st == PcanStatus.ReceiveQueueEmpty)
                break;
            if (st != PcanStatus.OK)
                break;
            var isFd = (pmsg.MsgType & MessageType.FlexibleDataRate) != 0;
            if (isFd)
            {
                // Skip FD frames in classic transceiver
                continue;
            }

            var isExt = (pmsg.MsgType & MessageType.Extended) != 0;
            var isRtr = (pmsg.MsgType & MessageType.RemoteRequest) != 0;
            var isErr = (pmsg.MsgType & MessageType.Error) != 0;

            byte[] data = pmsg.Data;
            if (data.Length > pmsg.DLC)
            {
                Array.Resize(ref data, pmsg.DLC);
            }

            var frame = new CanClassicFrame(pmsg.ID, data, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };

            // Assume PCAN timestamp is in microseconds. Convert to ticks (100ns)
            var ticks = ts * 10UL;
            list.Add(new CanReceiveData(frame) { recvTimestamp = ticks });
        }
        return list;
    }
}
