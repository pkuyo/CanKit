using System;
using System.Runtime.CompilerServices;
using CanKit.Adapter.PCAN.Native;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;

namespace CanKit.Adapter.PCAN.Transceivers;

public sealed class PcanClassicTransceiver : ITransceiver
{
    public unsafe uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)channel;
        uint sent = 0;
        var pMsg = stackalloc PcanBasicNative.TpcanMsg[1];
        foreach (var item in frames)
        {
            if (item is not CanClassicFrame cf)
            {
                throw new InvalidOperationException("PCAN classic transceiver requires CanClassicFrame.");
            }

            var type = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            if (cf.IsExtendedFrame)
            {
                type |= TPCANMessageType.PCAN_MESSAGE_EXTENDED;
            }
            if (cf.IsRemoteFrame)
            {
                type |= TPCANMessageType.PCAN_MESSAGE_RTR;
            }

            var dlc = (byte)Math.Min(8, (int)cf.Dlc);

            PcanStatus st;
            fixed (byte* ptr = item.Data.Span)
            {
                Unsafe.CopyBlock(pMsg->DATA, ptr, dlc);
                pMsg->ID = item.ID;
                pMsg->MSGTYPE = type;
                pMsg->LEN = dlc;
                st = (PcanStatus)PcanBasicNative.CAN_Write((UInt16)ch.Handle, pMsg);
            }

            if (st == PcanStatus.OK)
            {
                sent++;
            }
            else
            {
                PcanUtils.ThrowIfError(st, "Write(Classic)", $"PCAN: transmit frame failed. Channel:{((PcanBus)channel).Handle}");
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)bus;
        for (int i = 0; i < count; i++)
        {
            var st = PcanBasicNative.CAN_Read((UInt16)ch.Handle, out TPCANMsg pmsg, out var timestamp);
            if (st == TPCANStatus.PCAN_ERROR_QRCVEMPTY)
                break;
            if (st != TPCANStatus.PCAN_ERROR_OK)
            {
                PcanUtils.ThrowIfError((PcanStatus)st, "CAN_Read()", $"PCAN: receive frame failed. Channel:{((PcanBus)bus).Handle}");
            }
            var isExt = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_EXTENDED) != 0;
            var isRtr = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_RTR) != 0;
            var isErr = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_ERRFRAME) != 0;

            var len = Math.Min(pmsg.DATA.Length, pmsg.LEN);
            var slice = len == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(pmsg.DATA, 0, len);
            var frame = new CanClassicFrame(pmsg.ID, slice, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };

            // Assume PCAN timestamp is in microseconds. Convert to TimeSpan
            yield return new CanReceiveData(frame) { ReceiveTimestamp = timestamp.ToTimeSpan() };
        }
    }
}
