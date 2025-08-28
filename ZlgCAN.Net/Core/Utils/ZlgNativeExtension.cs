using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Native;
using static ZlgCAN.Net.Native.ZLGCAN;

namespace ZlgCAN.Net.Core.Utils
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

    }
}
