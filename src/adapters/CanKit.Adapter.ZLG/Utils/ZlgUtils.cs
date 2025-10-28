using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CanKit.Adapter.ZLG.Native;
using CanKit.Core.Definitions;
using static CanKit.Adapter.ZLG.Native.ZLGCAN;

namespace CanKit.Adapter.ZLG.Utils
{
    internal enum ZlgBaudRate : uint
    {
        ZLG_1M = 1_000_000,
        ZLG_800K = 800_000,
        ZLG_500K = 500_000,
        ZLG_250K = 250_000,
        ZLG_125K = 125_000,
        ZLG_100K = 100_000,
        ZLG_50K = 50_000,
    }

    internal enum ZlgDataDaudRate : uint
    {
        ZLG_5M = 5_000_000,
        ZLG_4M = 4_000_000,
        ZLG_2M = 2_000_000,
        ZLG_1M = 1_000_000,
        ZLG_800K = 800_000,
        ZLG_500K = 500_000,
        ZLG_250K = 250_000,
        ZLG_125K = 125_000,
        ZLG_100K = 100_000,
    }
    internal static class ZlgUtils
    {

        internal static unsafe void StructCopyToBuffer<T>(T src, byte* dst, uint count) where T : unmanaged
        {
            Unsafe.CopyBlockUnaligned(&src, dst, count);
        }

        internal static unsafe CanClassicFrame FromReceiveData(this in can_frame frame)
        {
            var ext = (frame.can_id & CAN_EFF_FLAG) != 0;
            var rtr = (frame.can_id & CAN_RTR_FLAG) != 0;
            var id = ext
                ? (frame.can_id & CAN_EFF_MASK)
                : (frame.can_id & CAN_SFF_MASK);
            var result = new CanClassicFrame((int)id, new byte[frame.can_dlc], ext, rtr);
            fixed (byte* dst = result.Data.Span)
            fixed (byte* src = frame.data)
            {
                Unsafe.CopyBlockUnaligned(dst, src, (uint)result.Data.Length);
            }
            return result;
        }

        internal static unsafe CanFdFrame FromReceiveData(this in canfd_frame frame)
        {
            var ext = (frame.can_id & CAN_EFF_FLAG) != 0;
            var id = ext
                ? (frame.can_id & CAN_EFF_MASK)
                : (frame.can_id & CAN_SFF_MASK);
            var result = new CanFdFrame((int)id, new byte[frame.len],
                (frame.flags & CANFD_BRS) != 0,
                (frame.flags & CANFD_ESI) != 0,
                ext);
            fixed (byte* dst = result.Data.Span)
            fixed (byte* src = frame.data)
            {
                Unsafe.CopyBlockUnaligned(dst, src, (uint)result.Data.Length);
            }
            return result;
        }

        internal static unsafe ZCAN_Transmit_Data ToTransmitData(this in CanClassicFrame frame, bool echo)
        {
            ZCAN_Transmit_Data data = new ZCAN_Transmit_Data();
            fixed (byte* ptr = frame.Data.Span)
            {
                data.frame.can_dlc = frame.Dlc;
                data.frame.can_id = frame.ToCanID();
                data.frame.__pad |= (byte)(echo ? TX_ECHO_FLAG : 0);
                Unsafe.CopyBlockUnaligned(data.frame.data, ptr, (uint)frame.Data.Length);
                data.transmit_type = 0;
            }
            return data;
        }

        internal static unsafe ZCAN_TransmitFD_Data ToTransmitData(this in CanFdFrame frame, bool echo)
        {
            ZCAN_TransmitFD_Data data = new ZCAN_TransmitFD_Data();
            fixed (byte* ptr = frame.Data.Span)
            {
                data.frame.len = (byte)frame.Data.Length;
                data.frame.can_id = frame.ToCanID();
                data.frame.flags |= (byte)(echo ? TX_ECHO_FLAG : 0);
                data.frame.flags |= (byte)(frame.BitRateSwitch ? CANFD_BRS : 0);
                data.frame.flags |= (byte)(frame.ErrorStateIndicator ? CANFD_ESI : 0);
                Unsafe.CopyBlockUnaligned(data.frame.data, ptr, (uint)frame.Data.Length);
                data.transmit_type = 0;
            }
            return data;
        }

        internal static unsafe void ToTransmitData(this in CanClassicFrame frame, bool echo, ZCAN_Transmit_Data* header, int offset)
        {
            var data = header + offset;
            fixed (byte* ptr = frame.Data.Span)
            {
                data->frame.can_dlc = frame.Dlc;
                data->frame.can_id = frame.ToCanID();
                data->frame.__pad |= (byte)(echo ? TX_ECHO_FLAG : 0);
                Unsafe.CopyBlockUnaligned(data->frame.data, ptr, frame.Dlc);
                data->transmit_type = 0;
            }
        }

        internal static unsafe void ToTransmitData(this in CanFdFrame frame, bool echo, ZCAN_TransmitFD_Data* header, int offset)
        {
            var data = header + offset;
            fixed (byte* ptr = frame.Data.Span)
            {
                data->frame.len = (byte)CanFdFrame.DlcToLen(frame.Dlc);
                data->frame.can_id = frame.ToCanID();
                data->frame.flags |= (byte)(echo ? TX_ECHO_FLAG : 0);
                data->frame.flags |= (byte)(frame.BitRateSwitch ? CANFD_BRS : 0);
                data->frame.flags |= (byte)(frame.ErrorStateIndicator ? CANFD_ESI : 0);
                Unsafe.CopyBlockUnaligned(data->frame.data, ptr, (uint)CanFdFrame.DlcToLen(frame.Dlc));
                data->transmit_type = 0;
            }

        }

        public static uint ToCanID(this in CanClassicFrame frame)
        {
            var id = (uint)frame.ID;
            var cid = frame.IsExtendedFrame ? ((id & CAN_EFF_MASK) | CAN_EFF_FLAG)
                : (id & CAN_SFF_MASK);
            if (frame.IsRemoteFrame) cid |= CAN_RTR_FLAG;
            if (frame.IsErrorFrame) cid |= CAN_ERR_FLAG;
            return cid;
        }
        public static uint ToCanID(this CanFdFrame frame)
        {
            var id = (uint)frame.ID;
            var cid = frame.IsExtendedFrame ? ((id & CAN_EFF_MASK) | CAN_EFF_FLAG)
                : (id & CAN_SFF_MASK);
            if (frame.IsErrorFrame) cid |= CAN_ERR_FLAG;
            return cid;
        }

        internal static unsafe void ToZCANObj(this ICanFrame frame, ZlgCanBus canBus, ZCANDataObj* pObj)
        {
            pObj->chnl = (byte)canBus.Options.ChannelIndex;
            pObj->dataType = 1;
            if (frame is CanClassicFrame classicFrame)
            {
                classicFrame.ToZCANData(canBus, pObj);
                return;
            }
            else if (frame is CanFdFrame canFdFrame)
            {
                canFdFrame.ToZCANData(canBus, pObj);
                return;
            }

            throw new NotSupportedException("Unsupported frame type for ZLGCAN");
        }

        private static unsafe void ToZCANData(this in CanClassicFrame frame, ZlgCanBus canBus, ZCANDataObj* pObj)
        {
            ref var data = ref pObj->data.fdData;
            fixed (byte* ptr = frame.Data.Span)
            {
                data.frame.can_id = frame.ToCanID();
                data.transmitType = (uint)(canBus.Options.TxRetryPolicy);
                data.frameType = 0;
                data.frame.len = (byte)CanFdFrame.DlcToLen(frame.Dlc);
                data.frame.can_id = frame.ToCanID();
                data.frame.flags |= (byte)(canBus.Options.WorkMode == ChannelWorkMode.Echo ? TX_ECHO_FLAG : 0);
                data.txEchoRequest = canBus.Options.WorkMode == ChannelWorkMode.Echo;
                fixed (byte* dst = data.frame.data)
                {
                    Unsafe.CopyBlockUnaligned(dst, ptr, frame.Dlc);
                }
            }
        }

        private static unsafe void ToZCANData(this in CanFdFrame frame, ZlgCanBus canBus, ZCANDataObj* pObj)
        {
            ref var data = ref pObj->data.fdData;
            fixed (byte* ptr = frame.Data.Span)
            {
                data.frame.can_id = frame.ToCanID();
                data.transmitType = (uint)(canBus.Options.TxRetryPolicy) + ((canBus.Options.WorkMode == ChannelWorkMode.Echo) ? 2u : 0u);
                data.frameType = 1;
                data.frame.len = (byte)CanFdFrame.DlcToLen(frame.Dlc);
                data.frame.can_id = frame.ToCanID();
                data.frame.flags |= (byte)(canBus.Options.WorkMode == ChannelWorkMode.Echo ? TX_ECHO_FLAG : 0);
                data.frame.flags |= (byte)(frame.BitRateSwitch ? CANFD_BRS : 0);
                data.frame.flags |= (byte)(frame.ErrorStateIndicator ? CANFD_ESI : 0);
                fixed (byte* dst = data.frame.data)
                {
                    Unsafe.CopyBlockUnaligned(dst, ptr, (uint)CanFdFrame.DlcToLen(frame.Dlc));
                }
            }
        }

        public static CanReceiveData FromZCANData(in ZCANDataObj pObj)
        {
            if (pObj.dataType == 1)
            {
                if (pObj.data.fdData.frameType == 1)
                    return FromZCANDataFd(pObj);
                else
                    return FromZCANDataClassic(pObj);
            }
            else if (pObj.dataType == 2)
            {
                return FromZCANDataErr(pObj);
            }
            throw new NotSupportedException("Unsupported frame type for ZLGCAN");
        }

        private static unsafe CanReceiveData FromZCANDataErr(in ZCANDataObj pObj)
        {
            var data = new byte[8];
            fixed (byte* ptr = data)
            {
                fixed (byte* src = pObj.data.data)
                {
                    Unsafe.CopyBlockUnaligned(ptr, src+8, 8);
                }
            }

            return new CanReceiveData(new CanClassicFrame(0, data)
            {
                IsErrorFrame = true
            });
        }

        private static unsafe CanReceiveData FromZCANDataClassic(in ZCANDataObj pObj)
        {
            var frame = pObj.data.fdData.frame;
            var rtr = (frame.can_id & ZLGCAN.CAN_RTR_FLAG) != 0;
            var ext = (frame.can_id & ZLGCAN.CAN_EFF_FLAG) != 0;
            var id = ext
                ? (frame.can_id & ZLGCAN.CAN_EFF_MASK)
                : (frame.can_id & ZLGCAN.CAN_SFF_MASK);
            var result = new CanClassicFrame((int)id, new byte[frame.len], ext, rtr);
            fixed (byte* ptr = result.Data.Span)
            {
                Unsafe.CopyBlockUnaligned(ptr, frame.data, (uint)result.Data.Length);
            }

            return new CanReceiveData(result);
        }
        private static unsafe CanReceiveData FromZCANDataFd(in ZCANDataObj pObj)
            => new CanReceiveData(pObj.data.fdData.frame.FromReceiveData());

        public static IntPtr ZLGHandle(this in BusNativeHandle handle) => handle.HandleValue;
    }
}
