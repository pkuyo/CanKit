using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Vector.Transceivers;

public sealed class VectorClassicTransceiver : IVectorTransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLevent[VxlApi.TX_BATCH_COUNT];
        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            if (frame is not CanClassicFrame classic)
                throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
            BuildEvent(vectorBus, classic, &events[index++]);
            if (index == VxlApi.TX_BATCH_COUNT)
            {
                count = VxlApi.TX_BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
                sent += (int)count;
                if(count != VxlApi.TX_BATCH_COUNT)
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

        var events = stackalloc VxlApi.XLevent[VxlApi.TX_BATCH_COUNT];
        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            if (frame is not CanClassicFrame classic)
                throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
            BuildEvent(vectorBus, classic, &events[index++]);
            if (index == VxlApi.TX_BATCH_COUNT)
            {
                count = VxlApi.TX_BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
                sent += (int)count;
                if(count != VxlApi.TX_BATCH_COUNT)
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
        => throw new InvalidOperationException();

    public unsafe bool ReceiveEvents(VectorBus bus, List<CanReceiveData> frames, List<ICanErrorInfo> errorInfos)
    {
        var ev = stackalloc VxlApi.XLevent[VxlApi.RX_BATCH_COUNT];
        var count = VxlApi.RX_BATCH_COUNT;
        var status = VxlApi.xlReceive(bus.Handle,ref count, ev);
        if (status == VxlApi.XL_ERR_QUEUE_IS_EMPTY)
        {
            return false;
        }

        if (!VectorErr.CheckIsInvalidOrThrow(status, "xlReceive()", bus))
        {
            return false;
        }
        for (int i = 0; i < count; i++)
        {
            if (TryProcessEvent(ev[i], out var data, out var info))
            {
                if(data != null)
                    frames.Add(data.Value);
                if(info != null)
                    errorInfos.Add(info);
            }
        }

        return true;
    }
    private bool TryProcessEvent(in VxlApi.XLevent nativeEvent, out CanReceiveData? data, out ICanErrorInfo? errorInfo)
    {
        data = null;
        errorInfo = null;

        switch ((VxlApi.XLeventType)nativeEvent.Tag)
        {
            case VxlApi.XLeventType.XL_RECEIVE_MSG:
                (data, errorInfo) = BuildReceiveData(nativeEvent.TagData.Msg);
                return true;

            case VxlApi.XLeventType.XL_TRANSMIT_MSG:
                (_, errorInfo) = BuildReceiveData(nativeEvent.TagData.Msg);
                return true;

            case VxlApi.XLeventType.XL_CHIP_STATE:
                errorInfo = BuildChipState(nativeEvent.TagData.ChipState);
                return true;

            default:
                return false;
        }
    }

    private static ICanErrorInfo BuildChipState(in VxlApi.XLchipStateBasic state)
    {
        CanControllerStatus controllerStatus;
        var busStatus = state.BusStatus;

        if ((busStatus & VxlApi.XL_CHIPSTAT_BUSOFF) != 0)
            controllerStatus = CanControllerStatus.TxPassive | CanControllerStatus.RxPassive;
        else if ((busStatus & VxlApi.XL_CHIPSTAT_ERROR_PASSIVE) != 0)
            controllerStatus = CanControllerStatus.TxPassive | CanControllerStatus.RxPassive;
        else if ((busStatus & VxlApi.XL_CHIPSTAT_ERROR_WARNING) != 0)
            controllerStatus = CanControllerStatus.TxWarning | CanControllerStatus.RxWarning;
        else
            controllerStatus = CanControllerStatus.Active;

        var counters = new CanErrorCounters
        {
            TransmitErrorCounter = state.TxErrorCounter,
            ReceiveErrorCounter = state.RxErrorCounter
        };

        return new DefaultCanErrorInfo(
            FrameErrorType.Controller,
            controllerStatus,
            CanProtocolViolationType.None,
            FrameErrorLocation.Unspecified,
            DateTime.UtcNow,
            state.BusStatus,
            null,
            FrameDirection.Unknown,
            null,
            CanTransceiverStatus.Unspecified,
            counters,
            null);
    }
    private static unsafe (CanReceiveData? receiveData, ICanErrorInfo? errorInfo) BuildReceiveData(in VxlApi.XLcanMsg msg)
    {
        var dlc = msg.Dlc;
        if (dlc > 8) dlc = 8; // classic CAN payload upper bound

        var payload = new byte[dlc];
        fixed (byte* src = msg.Data)
        fixed (byte* dst = payload)
        {
            Unsafe.CopyBlockUnaligned(dst, src, dlc);
        }

        var isExtended = (msg.Id & VxlApi.XL_CAN_EXT_MSG_ID) != 0;
        var isRtr = (msg.Flags & VxlApi.XL_CAN_MSG_FLAG_REMOTE_FRAME) != 0;
        var isError = (msg.Flags & VxlApi.XL_CAN_RXMSG_FLAG_EF) != 0;

        if (isError)
        {
            var errFrame = new CanClassicFrame((int)(msg.Id & (~VxlApi.XL_CAN_EXT_MSG_ID)), payload)
            {
                IsExtendedFrame = isExtended,
                IsRemoteFrame = isRtr,
                IsErrorFrame = true
            };
            FrameErrorType type = FrameErrorType.BusError;
            if ((msg.Flags & VxlApi.XL_CAN_RXMSG_FLAG_ARB_LOST) != 0)
                type |= FrameErrorType.ArbitrationLost;

            var errInfo = new DefaultCanErrorInfo(
                type,
                CanControllerStatus.Unknown,
                CanProtocolViolationType.Unknown,
                FrameErrorLocation.Unspecified,
                DateTime.UtcNow,
                msg.Flags,
                null,
                FrameDirection.Unknown,
                null,
                CanTransceiverStatus.Unspecified,
                null,
                errFrame);

            return (null, errInfo);
        }


        var frame = new CanClassicFrame((int)(msg.Id & 0x1FFFFFFF), payload)
        {
            IsExtendedFrame = isExtended,
            IsRemoteFrame = isRtr,
            IsErrorFrame = false
        };
        Console.WriteLine($"0x{frame.ID:X}, {frame.IsExtendedFrame}, 0x{(msg.Id & 0x1FFFFFFF):X}");

        return (new CanReceiveData(frame), null);
    }
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
