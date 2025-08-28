using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Devices;
using ZlgCAN.Net.Core.Factory;
using ZlgCAN.Net.Core.Models;
using ZlgCAN.Net.Native;

namespace USBCAN
{
    class Program
    {
   
        static unsafe void Main(string[] args)
        {
  
            using ICanDevice device = CanDeviceFactory.Create(DeviceType.ZCAN_USBCAN2,0);
            if (device.OpenDevice())
            {
                using ICanChannel channel = device.InitChannel(new ClassicCanChannelConfig()
                {
                    ChannelIndex = 0,
                });
                channel.Start();
                channel.Transmit(new ClassicCanFrame(0x18240801, null, true, true));
                var info = new ZLGCAN.ZCAN_CHANNEL_ERROR_INFO();
             
                ZLGCAN.ZCAN_ReadChannelErrInfo(channel.NativePtr,new IntPtr(&info));
                 
                uint recvCount = 0;
                while((recvCount = channel.CanReceiveCount(CanFrameFlag.Any)) == 0)
                {
                    Thread.Sleep(100);
                }
                var recv = channel.ReceiveAll();
                foreach (var data in recv)
                {
                    Console.WriteLine($"{data.Timestamp}, {data.canFrame.FrameKind}");
                }
            }
            else
            {
                Console.WriteLine("Open Failed");
            }
        }
    }
}
