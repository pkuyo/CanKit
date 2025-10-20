using System;
using System.Runtime.CompilerServices;
using CanKit.Adapter.PCAN.Native;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;

namespace CanKit.Adapter.PCAN.Transceivers;


public sealed class PcanFdTransceiver : ITransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames)
    {
        var ch = (PcanBus)bus;
        int sent = 0;
        var pMsg = stackalloc PcanBasicNative.TpcanMsgFd[1];
        foreach (var item in frames)
        {
            TPCANMessageType type;
            if (item is CanClassicFrame cf)
            {
                type = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                if (cf.IsRemoteFrame)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_RTR;
                }
            }
            else if (item is CanFdFrame fd)
            {
                type = TPCANMessageType.PCAN_MESSAGE_FD;
                if (fd.IsExtendedFrame)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_EXTENDED;
                }

                if (fd.BitRateSwitch)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_BRS;
                }

                if (fd.ErrorStateIndicator)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_ESI;
                }
            }
            else
            {
                throw new InvalidOperationException("PCAN classic transceiver requires CanClassicFrame/CanFdFrame.");
            }

            PcanStatus st;
            fixed (byte* ptr = item.Data.Span)
            {
                Unsafe.CopyBlock(pMsg->DATA, ptr, (uint)Math.Min(CanFdFrame.DlcToLen(item.Dlc), 64));
                pMsg->ID = (uint)item.ID;
                pMsg->MSGTYPE = type;
                pMsg->DLC = item.Dlc;
                st = (PcanStatus)PcanBasicNative.CAN_WriteFD((UInt16)ch.Handle, pMsg);
            }
            if (st == PcanStatus.OK)
            {
                sent++;
            }
            else if (st is PcanStatus.TransmitBufferFull or PcanStatus.TransmitQueueFull)
            {
                break;
            }
            else
            {
                PcanUtils.ThrowIfError(st, "Write(FD)", $"PCAN: transmit frame failed. Channel:{((PcanBus)bus).Handle}");
            }
        }
        return sent;
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<ICanFrame> frames)
    {
        var ch = (PcanBus)bus;
        int sent = 0;
        var pMsg = stackalloc PcanBasicNative.TpcanMsgFd[1];
        foreach (var item in frames)
        {
            TPCANMessageType type;
            if (item is CanClassicFrame cf)
            {
                type = TPCANMessageType.PCAN_MESSAGE_STANDARD;
                if (cf.IsRemoteFrame)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_RTR;
                }
            }
            else if (item is CanFdFrame fd)
            {
                type = TPCANMessageType.PCAN_MESSAGE_FD;
                if (fd.IsExtendedFrame)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_EXTENDED;
                }

                if (fd.BitRateSwitch)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_BRS;
                }

                if (fd.ErrorStateIndicator)
                {
                    type |= TPCANMessageType.PCAN_MESSAGE_ESI;
                }
            }
            else
            {
                throw new InvalidOperationException("PCAN classic transceiver requires CanClassicFrame/CanFdFrame.");
            }


            PcanStatus st;
            fixed (byte* ptr = item.Data.Span)
            {
                Unsafe.CopyBlock(pMsg->DATA, ptr, (uint)Math.Min(CanFdFrame.DlcToLen(item.Dlc), 64));
                pMsg->ID = (uint)item.ID;
                pMsg->MSGTYPE = type;
                pMsg->DLC = item.Dlc;
                st = (PcanStatus)PcanBasicNative.CAN_WriteFD((UInt16)ch.Handle, pMsg);
            }
            if (st == PcanStatus.OK)
            {
                sent++;
            }
            else if (st is PcanStatus.TransmitBufferFull or PcanStatus.TransmitQueueFull)
            {
                break;
            }
            else
            {
                PcanUtils.ThrowIfError(st, "Write(FD)", $"PCAN: transmit frame failed. Channel:{((PcanBus)bus).Handle}");
            }
        }
        return sent;
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in ICanFrame frame)
    {
        var ch = (PcanBus)bus;
        var pMsg = stackalloc PcanBasicNative.TpcanMsgFd[1];
        TPCANMessageType type;
        if (frame is CanClassicFrame cf)
        {
            type = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            if (cf.IsRemoteFrame)
            {
                type |= TPCANMessageType.PCAN_MESSAGE_RTR;
            }
        }
        else if (frame is CanFdFrame fd)
        {
            type = TPCANMessageType.PCAN_MESSAGE_FD;
            if (fd.IsExtendedFrame)
            {
                type |= TPCANMessageType.PCAN_MESSAGE_EXTENDED;
            }

            if (fd.BitRateSwitch)
            {
                type |= TPCANMessageType.PCAN_MESSAGE_BRS;
            }

            if (fd.ErrorStateIndicator)
            {
                type |= TPCANMessageType.PCAN_MESSAGE_ESI;
            }
        }
        else
        {
            throw new InvalidOperationException("PCAN classic transceiver requires CanClassicFrame/CanFdFrame.");
        }


        PcanStatus st;
        fixed (byte* ptr = frame.Data.Span)
        {
            Unsafe.CopyBlock(pMsg->DATA, ptr, (uint)Math.Min(CanFdFrame.DlcToLen(frame.Dlc), 64));
            pMsg->ID = (uint)frame.ID;
            pMsg->MSGTYPE = type;
            pMsg->DLC = frame.Dlc;
            st = (PcanStatus)PcanBasicNative.CAN_WriteFD((UInt16)ch.Handle, pMsg);
        }
        if (st == PcanStatus.OK)
        {
            return 1;
        }
        if (st is PcanStatus.TransmitBufferFull or PcanStatus.TransmitQueueFull)
        {
            return 0;
        }
        PcanUtils.ThrowIfError(st, "Write(FD)", $"PCAN: transmit frame failed. Channel:{((PcanBus)bus).Handle}");
        return 0;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int _ = 0)
    {
        var ch = (PcanBus)bus;
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < count; i++)
        {
            var st = PcanBasicNative.CAN_ReadFD((UInt16)ch.Handle, out TPCANMsgFD pmsg, out var timestamp);
            if (st == TPCANStatus.PCAN_ERROR_QRCVEMPTY)
                break;
            if (st != TPCANStatus.PCAN_ERROR_OK)
            {
                PcanUtils.ThrowIfError((PcanStatus)st, "CAN_ReadFD()", $"PCAN: receive frame failed. Channel:{((PcanBus)bus).Handle}");
            }

            var isFd = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_FD) != 0;
            var isExt = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_EXTENDED) != 0;
            var isErr = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_ERRFRAME) != 0;
            var ticks = timestamp * 10UL; // microseconds -> ticks (100ns)
            int len;
            if (!isFd)
            {
                var isRtr = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_RTR) != 0;

                len = Math.Min(pmsg.DATA.Length, pmsg.DLC);
                var scf = len == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(pmsg.DATA, 0, len);
                var cf = new CanClassicFrame((int)pmsg.ID, scf, isExt) { IsRemoteFrame = isRtr, IsErrorFrame = isErr };

                yield return new CanReceiveData(cf) { ReceiveTimestamp = TimeSpan.FromTicks((long)ticks) };
                continue;
            }

            var brs = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_BRS) != 0;
            var esi = (pmsg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_ESI) != 0;


            len = Math.Min(pmsg.DATA.Length, CanFdFrame.DlcToLen(pmsg.DLC));
            var sfd = len == 0 ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(pmsg.DATA, 0, len);
            var fd = new CanFdFrame((int)pmsg.ID, sfd, brs, esi) { IsExtendedFrame = isExt, IsErrorFrame = isErr };


            yield return new CanReceiveData(fd) { ReceiveTimestamp = TimeSpan.FromTicks((long)ticks) };
        }
    }
}
