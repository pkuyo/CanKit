using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Vector.Transceivers;

public sealed class VectorFdTransceiver : ITransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorFdTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLcanTxEvent[VxlApi.BATCH_COUNT];

        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            if (frame is not CanClassicFrame classic)
                throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
            BuildEvent(vectorBus, classic, &events[index++]);
            if (index == VxlApi.BATCH_COUNT)
            {
                count = VxlApi.BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, count, ref count, events), "xlCanTransmitEx");
                sent += (int)count;
                if(count != VxlApi.BATCH_COUNT)
                    return sent;
                index = 0;
            }
        }

        if (index != 0)
        {
            count = (uint)index;
            VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, count, ref count, events), "xlCanTransmitEx");
            sent += (int)count;
        }
        return sent;
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<ICanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorFdTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLcanTxEvent[VxlApi.BATCH_COUNT];

        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            if (frame is not CanClassicFrame classic)
                throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
            BuildEvent(vectorBus, classic, &events[index++]);
            if (index == VxlApi.BATCH_COUNT)
            {
                count = VxlApi.BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, count, ref count, events), "xlCanTransmitEx");
                sent += (int)count;
                if(count != VxlApi.BATCH_COUNT)
                    return sent;
                index = 0;
            }
        }

        if (index != 0)
        {
            count = (uint)index;
            VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, count, ref count, events), "xlCanTransmitEx");
            sent += (int)count;
        }
        return sent;
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ICanFrame[] frames)
        => Transmit(bus, frames.AsSpan());

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ArraySegment<ICanFrame> frames)
        => Transmit(bus, frames.AsSpan());

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in ICanFrame frame)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorFdTransceiver requires VectorBus.");
        var txEvent = stackalloc VxlApi.XLcanTxEvent[1];
        BuildEvent(vectorBus, frame, txEvent);
        uint sent = 0;
        VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, 1, ref sent, txEvent),
            "xlCanTransmitEx");
        return (int)sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
        => ((VectorBus)bus).ReceiveInternal(count);

    private static unsafe void BuildEvent(VectorBus bus, in ICanFrame frame, VxlApi.XLcanTxEvent* ev)
    {
        ev->Tag = VxlApi.XL_CAN_EV_TAG_TX_MSG;
        ev->ChanIndex = (byte)bus.Options.ChannelIndex;
        ev->TransId = 0xFFFF;

        ref var msg = ref ev->TagData.CanMsg;
        msg.CanId = (uint)frame.ID;
        if (frame.IsExtendedFrame)
            msg.CanId |= VxlApi.XL_CAN_EXT_MSG_ID;

        uint flags = 0;
        if (frame is CanFdFrame fd)
        {
            flags |= VxlApi.XL_CAN_TXMSG_FLAG_EDL;
            if (fd.BitRateSwitch)
                flags |= VxlApi.XL_CAN_TXMSG_FLAG_BRS;

            msg.Dlc = fd.Dlc;
            CopyData(fd.Data.Span.Slice(0, CanFdFrame.DlcToLen(fd.Dlc)), ref msg, CanFdFrame.DlcToLen(fd.Dlc));
        }
        else if (frame is CanClassicFrame classic)
        {
            if (classic.IsRemoteFrame)
                flags |= VxlApi.XL_CAN_TXMSG_FLAG_RTR;

            var length = Math.Min(classic.Data.Length, 8);
            msg.Dlc = (byte)length;
            CopyData(classic.Data.Span.Slice(0, length), ref msg, length);
        }
        else
        {
            throw new InvalidOperationException("Vector FD transceiver requires CanClassicFrame or CanFdFrame.");
        }

        msg.MsgFlags = flags;
    }

    private static unsafe void CopyData(ReadOnlySpan<byte> source, ref VxlApi.XLcanTxMsg msg, int length)
    {
        fixed (byte* dst = msg.Data)
        fixed (byte* src = source)
        {
            Unsafe.CopyBlockUnaligned(dst, src, (uint)length);
        }
    }
}
