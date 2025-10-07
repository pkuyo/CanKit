using System;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Transceivers;

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
                if (dlc == 0)
                {
                    payload = [];
                }
                else
                {
                    payload = new byte[dlc];
                    var src = cf.Data.Span;
                    src.Slice(0, dlc).CopyTo(payload);
                }
            }

            var msg = new PcanMessage(cf.ID, type, dlc, payload, extendedDataLength: false);

            var st = Api.Write(ch.Handle, msg);
            if (st == PcanStatus.OK)
            {
                sent++;
            }
            else
            {
                PcanUtils.ThrowIfError(st, "Write(Classic)",$"PCAN: transmit frame failed. Channel:{((PcanBus)channel).Handle}");
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)bus;
        var list = new List<CanReceiveData>();
        for (int i = 0; i < count; i++)
        {
            PcanMessage pmsg;
            ulong ts;
            var st = Api.Read(ch.Handle, out pmsg, out ts);
            if (st == PcanStatus.ReceiveQueueEmpty)
                break;
            if (st != PcanStatus.OK)
            {
                PcanUtils.ThrowIfError(st, "Read(FD)",$"PCAN: receive frame failed. Channel:{((PcanBus)bus).Handle}");
            }
            var isFd = (pmsg.MsgType & MessageType.FlexibleDataRate) != 0;
            if (isFd)
            {
                // Skip FD frames in classic transceiver
                continue;
            }

            var isExt = (pmsg.MsgType & MessageType.Extended) != 0;
            var isRtr = (pmsg.MsgType & MessageType.RemoteRequest) != 0;
            var isErr = (pmsg.MsgType & MessageType.Error) != 0;

            byte[] arr = pmsg.Data;
            var len = Math.Min(arr.Length, pmsg.DLC);
            var slice = len == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(arr, 0, len);
            var frame = new CanClassicFrame(pmsg.ID, slice, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };

            // Assume PCAN timestamp is in microseconds. Convert to TimeSpan
            var ticks = ts * 10UL; // microseconds -> ticks (100ns)
            list.Add(new CanReceiveData(frame) { ReceiveTimestamp = TimeSpan.FromTicks((long)ticks) });
        }
        return list;
    }
}
