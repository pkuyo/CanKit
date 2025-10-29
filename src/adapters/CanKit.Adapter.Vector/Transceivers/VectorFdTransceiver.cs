using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Vector.Transceivers;

public sealed class VectorFdTransceiver : IVectorTransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        if (bus is not VectorBus vectorBus)
            throw new InvalidOperationException("VectorFdTransceiver requires VectorBus.");

        var events = stackalloc VxlApi.XLcanTxEvent[VxlApi.TX_BATCH_COUNT];

        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            BuildEvent(vectorBus, frame, &events[index++]);
            if (index == VxlApi.TX_BATCH_COUNT)
            {
                count = VxlApi.TX_BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, count, ref count, events), "xlCanTransmitEx");
                sent += (int)count;
                if (count != VxlApi.TX_BATCH_COUNT)
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

        var events = stackalloc VxlApi.XLcanTxEvent[VxlApi.TX_BATCH_COUNT];

        var index = 0;
        uint count;
        int sent = 0;
        foreach (var frame in frames)
        {
            BuildEvent(vectorBus, frame, &events[index++]);
            if (index == VxlApi.TX_BATCH_COUNT)
            {
                count = VxlApi.TX_BATCH_COUNT;
                VectorErr.ThrowIfError(VxlApi.xlCanTransmitEx(vectorBus.Handle, vectorBus.AccessMask, count, ref count, events), "xlCanTransmitEx");
                sent += (int)count;
                if (count != VxlApi.TX_BATCH_COUNT)
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
        => throw new InvalidOperationException();

    public bool ReceiveEvents(VectorBus bus, List<CanReceiveData> frames, List<ICanErrorInfo> errorInfos)
    {
        bool hasAny = false;
        for (int i = 0; i < VxlApi.RX_BATCH_COUNT; i++)
        {
            var status = VxlApi.xlCanReceive(bus.Handle, out var nativeEvent);
            if (status == VxlApi.XL_ERR_QUEUE_IS_EMPTY)
            {
                break;
            }

            if (!VectorErr.CheckIsInvalidOrThrow(status, "xlCanReceive(loop)", bus))
            {
                return false;
            }
            if (TryProcessEvent(nativeEvent, bus, out var frame, out var errorInfo,
                    bus.Options.WorkMode == ChannelWorkMode.Echo))
            {
                hasAny = true;
                if (frame is not null)
                    frames.Add(frame.Value);
                if (errorInfo is not null)
                    errorInfos.Add(errorInfo);
            }
        }
        return hasAny;
    }

    private bool TryProcessEvent(in VxlApi.XLcanRxEvent nativeEvent, ICanBus bus, out CanReceiveData? data, out ICanErrorInfo? errorInfo, bool receiveEcho)
    {
        data = null;
        errorInfo = null;
        var timeSpan = TimeSpanEx.FromNanoseconds(nativeEvent.TimeStamp);
        switch (nativeEvent.Tag)
        {
            case VxlApi.XL_CAN_EV_TAG_RX_OK:

                data = BuildReceiveData(nativeEvent.TagData.CanRxOkMsg, bus, timeSpan, false);
                return true;
            case VxlApi.XL_CAN_EV_TAG_TX_OK:
                if (receiveEcho)
                {
                    data = BuildReceiveData(nativeEvent.TagData.CanTxOkMsg, bus, timeSpan, true);
                }
                return false;
            case VxlApi.XL_CAN_EV_TAG_CHIP_STATE:
                errorInfo = BuildChipState(nativeEvent.TagData.ChipState);
                return true;

            case VxlApi.XL_CAN_EV_TAG_RX_ERROR:
                errorInfo = BuildGenericError(nativeEvent.Tag, nativeEvent.TagData, timeSpan, false);
                return true;
            case VxlApi.XL_CAN_EV_TAG_TX_ERROR:
                errorInfo = BuildGenericError(nativeEvent.Tag, nativeEvent.TagData, timeSpan, true);
                return true;

            default:
                return false;
        }
    }

    private static CanReceiveData BuildReceiveData(in VxlApi.XLcanRxMsg msg, ICanBus bus, TimeSpan timeSpan, bool isEcho)
    {
        var isExtended = (msg.CanId & VxlApi.XL_CAN_EXT_MSG_ID) != 0;
        var flags = msg.MsgFlags;
        var isFd = (flags & VxlApi.XL_CAN_RXMSG_FLAG_EDL) != 0;

        var length = isFd ? CanFdFrame.DlcToLen(msg.Dlc) : Math.Min(msg.Dlc, (byte)8);
        var payload = bus.Options.BufferAllocator.Rent(length);

        unsafe
        {
            fixed (byte* src = msg.Data)
            fixed (byte* dst = payload.Memory.Span)
            {
                Unsafe.CopyBlockUnaligned(dst, src, (uint)length);
            }
        }

        if (isFd)
        {
            var frame = new CanFdFrame((int)(msg.CanId & 0x1FFFFFFF), payload,
                (flags & VxlApi.XL_CAN_RXMSG_FLAG_BRS) != 0,
                (flags & VxlApi.XL_CAN_RXMSG_FLAG_ESI) != 0,
                isExtended,
                ownMemory: bus.Options.BufferAllocator.FrameNeedDispose)
            { IsErrorFrame = (flags & VxlApi.XL_CAN_RXMSG_FLAG_EF) != 0 };
            return new CanReceiveData(frame)
            {
                IsEcho = isEcho,
                ReceiveTimestamp = timeSpan
            };
        }
        else
        {
            var frame = new CanClassicFrame((int)(msg.CanId & 0x1FFFFFFF), payload)
            {
                IsExtendedFrame = isExtended,
                IsRemoteFrame = (flags & VxlApi.XL_CAN_RXMSG_FLAG_RTR) != 0,
                IsErrorFrame = (flags & VxlApi.XL_CAN_RXMSG_FLAG_EF) != 0
            };
            return new CanReceiveData(frame)
            {
                IsEcho = isEcho,
                ReceiveTimestamp = timeSpan
            };
        }
    }

    private static ICanErrorInfo BuildChipState(in VxlApi.XLchipState state)
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

    private static ICanErrorInfo BuildGenericError(ushort tag, in VxlApi.XLcanRxTagData data, TimeSpan timeSpan, bool isTx)
    {
        byte errorCode;
        try
        {
            errorCode = data.CanError.ErrorCode;
        }
        catch
        {
            errorCode = (byte)(tag & 0xFF);
        }

        return new DefaultCanErrorInfo(
            FrameErrorType.BusError,
            CanControllerStatus.Unknown,
            CanProtocolViolationType.Unknown,
            FrameErrorLocation.Unspecified,
            DateTime.UtcNow,
            errorCode,
            timeSpan,
            isTx ? FrameDirection.Tx : FrameDirection.Rx,
            null,
            CanTransceiverStatus.Unspecified,
            null,
            null);
    }


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
