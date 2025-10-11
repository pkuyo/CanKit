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
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<ICanFrame> frames, int timeOut = 0)
    {
        _ = timeOut;
        var ch = (PcanBus)channel;
        int sent = 0;
        var pMsg = stackalloc PcanBasicNative.TpcanMsgFd[1];
        foreach (var item in frames)
        {
            if (item is not CanFdFrame fd)
            {
                throw new InvalidOperationException("PCAN FD transceiver requires CanFdFrame.");
            }

            var type = TPCANMessageType.PCAN_MESSAGE_FD;
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


            PcanStatus st;
            fixed (byte* ptr = item.Data.Span)
            {
                Unsafe.CopyBlock(pMsg->DATA, ptr, (uint)Math.Min(CanFdFrame.DlcToLen(fd.Dlc),64));
                pMsg->ID = (uint)item.ID;
                pMsg->MSGTYPE = type;
                pMsg->DLC = fd.Dlc;
                st = (PcanStatus)PcanBasicNative.CAN_WriteFD((UInt16)ch.Handle, pMsg);
            }
            if (st == PcanStatus.OK)
            {
                sent++;
            }
            else
            {
                PcanUtils.ThrowIfError(st, "Write(FD)", $"PCAN: transmit frame failed. Channel:{((PcanBus)channel).Handle}");
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        _ = timeOut;
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
            var fd = new CanFdFrame(unchecked((int)pmsg.ID), sfd, brs, esi) { IsExtendedFrame = isExt, IsErrorFrame = isErr };


            yield return new CanReceiveData(fd) { ReceiveTimestamp = TimeSpan.FromTicks((long)ticks) };
        }
    }
}
