using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Vector.Transceivers;

public sealed class VectorClassicTransceiver : IVectorTransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<CanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLevent[VxlApi.TX_BATCH_COUNT];
        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            if (frame.FrameKind is not CanFrameType.Can20)
                throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
            BuildEvent(vectorBus, frame, &events[index++]);
            if (index == VxlApi.TX_BATCH_COUNT)
            {
                count = VxlApi.TX_BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
                sent += (int)count;
                if (count != VxlApi.TX_BATCH_COUNT)
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

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<CanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLevent[VxlApi.TX_BATCH_COUNT];
        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            if (frame.FrameKind is not CanFrameType.Can20)
                throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
            BuildEvent(vectorBus, frame, &events[index++]);
            if (index == VxlApi.TX_BATCH_COUNT)
            {
                count = VxlApi.TX_BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmit(vectorBus.Handle, vectorBus.AccessMask, ref count, events), "xlCanTransmit");
                sent += (int)count;
                if (count != VxlApi.TX_BATCH_COUNT)
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

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, CanFrame[] frames)
        => Transmit(bus, frames.AsSpan());

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ArraySegment<CanFrame> frames)
        => Transmit(bus, frames.AsSpan());

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frame)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorClassicTransceiver requires VectorBus.");
        if (frame.FrameKind is not CanFrameType.Can20)
            throw new InvalidOperationException("Vector classic transceiver requires CanClassicFrame.");
        var pEv = stackalloc VxlApi.XLevent[1];
        BuildEvent(vectorBus, frame, pEv);
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
        var status = VxlApi.xlReceive(bus.Handle, ref count, ev);
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
            if (TryProcessEvent(ev[i], bus, out var data, out var info, bus.Options.WorkMode == ChannelWorkMode.Echo))
            {
                if (data != null)
                    frames.Add(data.Value);
                if (info != null)
                    errorInfos.Add(info);
            }
        }

        return true;
    }
    private bool TryProcessEvent(in VxlApi.XLevent nativeEvent, ICanBus bus, out CanReceiveData? data, out ICanErrorInfo? errorInfo, bool echo)
    {
        data = null;
        errorInfo = null;
        var timeSpan = TimeSpanEx.FromNanoseconds(nativeEvent.TimeStamp);
        switch ((VxlApi.XLeventType)nativeEvent.Tag)
        {
            case VxlApi.XLeventType.XL_RECEIVE_MSG:
                (data, errorInfo) = BuildReceiveData(nativeEvent.TagData.Msg, bus, echo, false, timeSpan);
                return true;

            case VxlApi.XLeventType.XL_TRANSMIT_MSG:
                (data, errorInfo) = BuildReceiveData(nativeEvent.TagData.Msg, bus, echo, true, timeSpan);
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
            CanTransceiverStatus.Unknown,
            DateTime.UtcNow,
            state.BusStatus,
            null,
            FrameDirection.Unknown,
            null,
            counters,
            null);
    }
    private static unsafe (CanReceiveData? receiveData, ICanErrorInfo? errorInfo)
        BuildReceiveData(in VxlApi.XLcanMsg msg, ICanBus bus, bool recEcho, bool isEcho, TimeSpan timeSpan)
    {
        var dlc = msg.Dlc;
        if (dlc > 8) dlc = 8; // classic CAN payload upper bound

        var payload = bus.Options.BufferAllocator.Rent(dlc);
        fixed (byte* src = msg.Data)
        fixed (byte* dst = payload.Memory.Span)
        {
            Unsafe.CopyBlockUnaligned(dst, src, dlc);
        }

        var isExtended = (msg.Id & VxlApi.XL_CAN_EXT_MSG_ID) != 0;
        var isRtr = (msg.Flags & VxlApi.XL_CAN_MSG_FLAG_REMOTE_FRAME) != 0;
        var isError = (msg.Flags & VxlApi.XL_CAN_RXMSG_FLAG_EF) != 0;

        if (isError)
        {
            var errFrame = CanFrame.Classic((int)(msg.Id & (~VxlApi.XL_CAN_EXT_MSG_ID)), payload, isExtended,
                isRtr,
                bus.Options.BufferAllocator.FrameNeedDispose, true);
            FrameErrorType type = FrameErrorType.BusError;
            if ((msg.Flags & VxlApi.XL_CAN_RXMSG_FLAG_ARB_LOST) != 0)
                type |= FrameErrorType.ArbitrationLost;

            var errInfo = new DefaultCanErrorInfo(
                type,
                CanControllerStatus.Unknown,
                CanProtocolViolationType.Unknown,
                FrameErrorLocation.Unspecified,
                CanTransceiverStatus.Unknown,
                DateTime.UtcNow,
                msg.Flags,
                timeSpan,
                isEcho ? FrameDirection.Tx : FrameDirection.Rx,
                null,
                null,
                errFrame);

            return (null, errInfo);
        }

        if (recEcho && isEcho)
        {
            return (null, null);
        }

        var frame = CanFrame.Classic((int)(msg.Id & 0x1FFFFFFF), payload, isExtended,
                isRtr, bus.Options.BufferAllocator.FrameNeedDispose);
        return (new CanReceiveData(frame)
        {
            ReceiveTimestamp = timeSpan,
            IsEcho = isEcho

        }, null);
    }
    private static unsafe void BuildEvent(VectorBus bus, in CanFrame frame, VxlApi.XLevent* ev)
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
        fixed (byte* src = frame.Data.Span)
        {
            Unsafe.CopyBlockUnaligned(dst, src, (uint)length);
        }
    }
}
