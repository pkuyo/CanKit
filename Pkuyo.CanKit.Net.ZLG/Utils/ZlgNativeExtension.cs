using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using static Pkuyo.CanKit.ZLG.Native.ZLGCAN;

namespace Pkuyo.CanKit.ZLG.Utils
{
    public static class ZlgNativeExtension
    {
        internal static IEnumerable<CanReceiveData> RecvCanFrames(ZCANDataObj[] recvData, int receiveCount)
        {
       
            for (int i = 0; i < receiveCount; i++)
            {
                CanReceiveData receiveData = null;
                var recData = recvData[i];
                var typeFlag = GetFrameType(recData.dataType);
                
                unsafe
                {
                    if ((typeFlag & CanFrameType.CanClassic) != 0 || (typeFlag & CanFrameType.CanFd) != 0)
                    {
                        var data = ByteArrayToStruct<ZCANCANFDData>(recData.data);
                        if (data.frameType == 1)
                        {
                            receiveData = new CanReceiveData()
                            {
                                timestamp = data.timeStamp,
                                canFrame = new CanFdFrame(data.frame.flags,data.frame.can_id, ToArray(data.frame.data, data.frame.len))
                            };
                        }
                        else
                        {
                            receiveData = new CanReceiveData()
                            {
                                timestamp = data.timeStamp,
                                canFrame = new CanClassicFrame(data.frame.can_id, ToArray(data.frame.data, data.frame.len))
                            };
                        }
                    }
                }
                if (receiveData != null)
                    yield return receiveData;
            }
        }

        internal static ZCANDataObj[] TransmitCanFrames(CanTransmitData[] canFrames, byte channelId)
        {
            ZCANDataObj[] transmitData = new ZCANDataObj[canFrames.Length];
            for(int i = 0; i< canFrames.Length;i++)
            {
                transmitData[i] = canFrames[i].canFrame.ToZCANObj(channelId);
            }
            return transmitData;
        }
        
  

        internal static CanFrameType GetFrameType(uint dataType)
        {
            if(dataType == 0 || dataType > 8)
            {
                return CanFrameType.Invalid;
            }
            if (dataType == 1)
                return (CanFrameType)(1 | 2);
            return (CanFrameType)(1 <<(int)dataType);

        }

        internal static byte GetRawFrameType(CanFrameType type)
        {
            if ((type & CanFrameType.CanClassic) != 0)
                return 1;
            if ((type & CanFrameType.CanFd) != 0)
                return 1;
            if ((type & CanFrameType.Error) != 0)
                return 2;
            if ((type & CanFrameType.Gps) != 0)
                return 3;
            if ((type & CanFrameType.Lin) != 0)
                return 4;
            if ((type & CanFrameType.BusStage) != 0)
                return 5;
            if ((type & CanFrameType.LinError) != 0)
                return 6;
            if ((type & CanFrameType.LinEx) != 0)
                return 7;
            if ((type & CanFrameType.LinEvent) != 0)
                return 8;
            return 0;
        }

        internal static unsafe byte[] ToArray(byte* data, int length)
        {
            var arr = new byte[length];
            Marshal.Copy((IntPtr)data, arr, 0, length);
            
            return arr;
        }
        internal static unsafe T ByteArrayToStruct<T>(byte* data) where T : unmanaged
        {
             return *(T*)data;    
        }
        internal static unsafe void StructCopyToBuffer<T>(T src, byte* dst, uint count) where T : unmanaged
        {
            Unsafe.CopyBlockUnaligned(&src, dst, 92);
        }

               internal static unsafe CanClassicFrame FromReceiveData(this ZLGCAN.can_frame frame)
        {
            var result = new CanClassicFrame(frame.can_id,new byte[frame.can_dlc]);
            fixed (byte* ptr = result.Data)
            {
                Unsafe.CopyBlockUnaligned(frame.data,ptr, (uint)result.DataLen);
            }
            return result;
        }
        
        internal static unsafe ZLGCAN.ZCAN_Transmit_Data ToTransmitData(this CanClassicFrame frame)
        {
            ZLGCAN.ZCAN_Transmit_Data data = new ZLGCAN.ZCAN_Transmit_Data();
            fixed (byte* ptr = frame.Data)
            {
                data.frame.can_dlc = frame.Dlc;
                data.frame.can_id = frame.RawID;
                Unsafe.CopyBlockUnaligned(ptr, data.frame.data, (uint)frame.DataLen);
                data.transmit_type = 0; //TODO: 不清楚功能
            }
            return data;
        }

        internal static ZLGCAN.ZCANDataObj ToZCANObj(this CanFrameBase frame, byte channelID)
        {
            if (frame is CanClassicFrame classicFrame)
                return classicFrame.ToZCANObj(channelID);
            throw new NotSupportedException("Unsupported frame type for ZLGCAN");
        }
        internal static unsafe ZLGCAN.ZCANDataObj ToZCANObj(this CanClassicFrame frame, byte channelID)
        {
            ZLGCAN.ZCANDataObj obj = new ZLGCAN.ZCANDataObj
            {
                dataType = frame.FrameType(),
                chnl = channelID
            };
          
            fixed (byte * ptr = frame.Data)
            {
                var data = new ZCANCANFDData()
                {
                    frameType = 0,
                    timeStamp = 0,
                    frame = new ZLGCAN.canfd_frame()
                    {
                        can_id = frame.RawID,
                        len = (byte)frame.DataLen,
                    }
                };
                if (frame is CanFdFrame frame2)
                    data.frame.flags = frame2.Flag; //TODO: 检查是不是格式理解错了
                Unsafe.CopyBlockUnaligned(ptr, data.frame.data, (uint)frame.DataLen);
                StructCopyToBuffer(data, obj.data, 92);

            }
            return obj;
        }
        
        internal static byte FrameType(this CanFrameBase frame) => ZlgNativeExtension.GetRawFrameType(frame.FrameKind);
    }
}
