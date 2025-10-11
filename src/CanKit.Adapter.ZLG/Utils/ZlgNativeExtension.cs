using System;
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
    internal static class ZlgNativeExtension
    {

        internal static byte GetRawFrameType(CanFrameType type)
        {
            if ((type & CanFrameType.Can20) != 0)
                return 1;
            if ((type & CanFrameType.CanFd) != 0)
                return 1;
            return 0;
        }

        internal static unsafe void StructCopyToBuffer<T>(T src, byte* dst, uint count) where T : unmanaged
        {
            Unsafe.CopyBlockUnaligned(&src, dst, count);
        }

        internal static unsafe CanClassicFrame FromReceiveData(this can_frame frame)
        {
            var result = new CanClassicFrame((frame.can_id & ZLGCAN.CAN_EFF_MASK) == 1 ?
                frame.can_id & ZLGCAN.CAN_EFF_MASK :
                frame.can_id & ZLGCAN.CAN_SFF_MASK, new byte[frame.can_dlc]);
            fixed (byte* ptr = result.Data.Span)
            {
                Unsafe.CopyBlockUnaligned(ptr, frame.data, (uint)result.Data.Length);
            }
            return result;
        }

        internal static unsafe CanFdFrame FromReceiveData(this canfd_frame frame)
        {
            var result = new CanFdFrame((frame.can_id & ZLGCAN.CAN_EFF_MASK) == 1 ?
                    frame.can_id & ZLGCAN.CAN_EFF_MASK :
                    frame.can_id & ZLGCAN.CAN_SFF_MASK, new byte[frame.len],
                (frame.flags & CANFD_BRS) != 0,
                (frame.flags & CANFD_ESI) != 0);
            fixed (byte* ptr = result.Data.Span)
            {
                Unsafe.CopyBlockUnaligned(ptr, frame.data, (uint)result.Data.Length);
            }
            return result;
        }

        internal static unsafe ZCAN_Transmit_Data ToTransmitData(this CanClassicFrame frame, bool echo)
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

        internal static unsafe ZCAN_TransmitFD_Data ToTransmitData(this CanFdFrame frame, bool echo)
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

        internal static ZLGCAN.ZCANDataObj ToZCANObj(this ICanFrame frame, byte channelID)
        {
            if (frame is CanClassicFrame classicFrame)
                return classicFrame.ToZCANObj(channelID);
            throw new NotSupportedException("Unsupported frame type for ZLGCAN");
        }

        internal static unsafe ZLGCAN.ZCANDataObj ToZCANObj(this CanClassicFrame frame, byte channelID)
        {
            ZLGCAN.ZCANDataObj obj = new ZLGCAN.ZCANDataObj
            {
                dataType = GetRawFrameType(CanFrameType.Can20),
                chnl = channelID
            };

            fixed (byte* ptr = frame.Data.Span)
            {
                var data = new ZCANCANFDData
                {
                    frameType = 0,
                    timeStamp = 0,
                    frame = new canfd_frame
                    {
                        can_id = frame.ToCanID(),
                        len = (byte)frame.Data.Length,
                    }
                };
                Unsafe.CopyBlockUnaligned(data.frame.data, ptr, (uint)frame.Data.Length);
                StructCopyToBuffer(data, obj.data, 92);

            }
            return obj;
        }

        public static uint ToCanID(this CanClassicFrame frame)
        {
            var id = frame.ID;
            var cid = frame.IsExtendedFrame ? ((frame.ID & CAN_EFF_MASK) | CAN_EFF_FLAG)
                : (frame.ID & CAN_SFF_MASK);
            if (frame.IsErrorFrame) cid |= CAN_RTR_FLAG;
            if (frame.IsErrorFrame) cid |= CAN_ERR_FLAG;
            return cid;
        }
        public static uint ToCanID(this CanFdFrame frame)
        {
            var id = frame.ID;
            var cid = frame.IsExtendedFrame ? ((frame.ID & CAN_EFF_MASK) | CAN_EFF_FLAG)
                : (frame.ID & CAN_SFF_MASK);
            if (frame.IsErrorFrame) cid |= CAN_ERR_FLAG;
            return cid;
        }
    }
}
