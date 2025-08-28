using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ZlgCAN.Net.Core.Models;
using ZlgCAN.Net.Native;
using static ZlgCAN.Net.Native.ZLGCAN;

namespace ZlgCAN.Net.Core.Utils
{
    public static class ZlgNativeExtension
    {
        internal static unsafe IEnumerable<CanReceiveData> RecvCanFrames(IntPtr recvPtr, int receiveCount, CanFrameFlag filterFlag)
        {
       
            for (int i = 0; i < receiveCount; i++)
            {
                CanReceiveData reciveData = null;
                var recData = (ZCANDataObj)Marshal.PtrToStructure(recvPtr + i * Marshal.SizeOf(typeof(ZCANDataObj)), typeof(ZCANDataObj));
                var typeFlag = GetFrameFlag(recData.dataType);
                unsafe
                {
                    if ((typeFlag & CanFrameFlag.ClassicCan) != 0 || (typeFlag & CanFrameFlag.CanFd) != 0)
                    {
                        var data = ByteArrayToStruct<ZCANCANFDData>(recData.data);
                        if (data.frameType == 1)
                        {
                            reciveData = new CanReceiveData()
                            {
                                timestamp = data.timeStamp,
                                canFrame = new FdCanFrame(data.frame.flags,data.frame.can_id, ToArray(data.frame.data, data.frame.len))
                            };
                        }
                        else
                        {
                            reciveData = new CanReceiveData()
                            {
                                timestamp = data.timeStamp,
                                canFrame = new ClassicCanFrame(data.frame.can_id, ToArray(data.frame.data, data.frame.len))
                            };
                        }
                    }
                }
                if (reciveData != null)
                    yield return reciveData;
            }
        }

        internal unsafe static IntPtr TransmitCanFrames(CanFrameBase[] canFrames, byte channelId)
        {
            ZCANDataObj* p2zcanReceiveData = (ZCANDataObj*)Marshal.AllocHGlobal((int)(Marshal.SizeOf(typeof(ZLGCAN.ZCANDataObj)) * canFrames.Length));
            for(int i = 0; i< canFrames.Length;i++)
            {
                p2zcanReceiveData[i] = canFrames[i].ToZCANObj(channelId);
            }
            return new IntPtr(p2zcanReceiveData);
        }

        internal static CanFrameFlag GetFrameFlag(uint dataType)
        {
            if(dataType == 0 || dataType > 8)
            {
                return CanFrameFlag.Invalid;
            }
            if (dataType == 1)
                return (CanFrameFlag)(1 | 2);
            return (CanFrameFlag)(1 <<(int)dataType);

        }

        internal static byte GetFrameType(CanFrameFlag flag)
        {
            if ((flag & CanFrameFlag.ClassicCan) != 0)
                return 1;
            if ((flag & CanFrameFlag.CanFd) != 0)
                return 1;
            if ((flag & CanFrameFlag.Error) != 0)
                return 2;
            if ((flag & CanFrameFlag.Gps) != 0)
                return 3;
            if ((flag & CanFrameFlag.Lin) != 0)
                return 4;
            if ((flag & CanFrameFlag.BusStage) != 0)
                return 5;
            if ((flag & CanFrameFlag.LinError) != 0)
                return 6;
            if ((flag & CanFrameFlag.LinEx) != 0)
                return 7;
            if ((flag & CanFrameFlag.LinEvent) != 0)
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
