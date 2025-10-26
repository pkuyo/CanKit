using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Vector.Transceivers;

public sealed class VectorClassicTransceiver : ITransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLevent[VxlApi.BATCH_COUNT];
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
                VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
                sent += (int)count;
                if(count != VxlApi.BATCH_COUNT)
                    return sent;
                index = 0;
            }
        }

        if (index != 0)
        {
            count = (uint)index;
            VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
            sent += (int)count;
        }

        return sent;
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<ICanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLevent[VxlApi.BATCH_COUNT];
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
                VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
                sent += (int)count;
                if(count != VxlApi.BATCH_COUNT)
                    return sent;
                index = 0;
            }
        }

        if (index != 0)
        {
            count = (uint)index;
            VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
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
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");
        if (frame is not CanClassicFrame classic)
            throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
        var pEv = stackalloc VxlApi.XLevent[1];
        BuildEvent(vectorBus, classic, pEv);
        uint count = 1;
        VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, pEv), "xlCanTransmit");
        return (int)count;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
        => ((VectorBus)bus).ReceiveInternal(count);

    private static unsafe void BuildEvent(VectorBus bus, in CanClassicFrame frame, VxlApi.XLevent* ev)
    {
        ev->Tag = VxlApi.XL_EVENT_TAG_TRANSMIT_MSG;
        ev->ChanIndex = (byte)bus.Options.ChannelIndex;
        ev->TransId = 0xFFFF;
        ev->Flags = 0;
        ev->Reserved = 0;
        ev->TimeStamp = 0;

        ref var msg = ref ev->TagData.Msg;
        msg.Id = (uint)frame.ID;
        if (frame.IsExtendedFrame)
            msg.Id |= VxlApi.XL_CAN_EXT_MSG_ID;

        msg.Flags = frame.IsRemoteFrame ? VxlApi.XL_CAN_MSG_FLAG_REMOTE_FRAME : (ushort)0;

        var length = Math.Min(frame.Data.Length, 8);
        msg.Dlc = (ushort)length;

        fixed (byte* dst = msg.Data)
        fixed(byte* src = frame.Data.Span)
        {
            Unsafe.CopyBlockUnaligned(dst, src, (uint)length);
        }
    }
}

