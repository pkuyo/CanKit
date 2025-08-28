using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZlgCAN.Net.Core.Models;

namespace ZlgCAN.Net.Core.Abstractions
{
    public interface ICanDevice : IDisposable
    {

        bool OpenDevice();

        void CloseDevice();

        ICanChannel InitChannel(CanChannelConfig config);


        CanDeviceInfo DeviceInfo { get; }

        IntPtr NativePtr { get; }

        bool IsDeviceOpen { get; }
    }
}
