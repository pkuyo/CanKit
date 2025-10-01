using System;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Transceivers;


public sealed class PcanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)channel;
        uint sent = 0;
        foreach (var item in frames)
        {
            if (item.CanFrame is not CanFdFrame fd)
            {
                throw new InvalidOperationException("PCAN FD transceiver requires CanFdFrame.");
            }

            var type = MessageType.FlexibleDataRate;
            if (fd.IsExtendedFrame)
            {
                type |= MessageType.Extended;
            }
            if (fd.BitRateSwitch)
            {
                type |= MessageType.BitRateSwitch;
            }
            if (fd.ErrorStateIndicator)
            {
                type |= MessageType.ErrorStateIndicator;
            }

            var payload = fd.Data.Length == 0 ? Array.Empty<byte>() : fd.Data.ToArray();
            var msg = new PcanMessage(fd.ID, type, fd.Dlc, payload, extendedDataLength: true);

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
            if (!isFd)
            {
                // Skip classic frames in FD transceiver
                continue;
            }

            var isExt = (pmsg.MsgType & MessageType.Extended) != 0;
            var brs = (pmsg.MsgType & MessageType.BitRateSwitch) != 0;
            var esi = (pmsg.MsgType & MessageType.ErrorStateIndicator) != 0;
            var isErr = (pmsg.MsgType & MessageType.Error) != 0;

            byte[] arr = pmsg.Data;
            var len = Math.Min(arr.Length, CanFdFrame.DlcToLen(pmsg.DLC));
            var slice = len == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(arr, 0, len);
            var frame = new CanFdFrame(pmsg.ID, slice, brs, esi) { IsExtendedFrame = isExt, IsErrorFrame = isErr };

            var ticks = ts * 10UL;
            list.Add(new CanReceiveData(frame) { RecvTimestamp = ticks });
        }
        return list;
    }
}
