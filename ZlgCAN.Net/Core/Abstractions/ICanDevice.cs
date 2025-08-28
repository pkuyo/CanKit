using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions
{
    public interface ICanDevice : IDisposable
    {

        bool OpenDevice();

        void CloseDevice();
        
        CanDeviceInfo DeviceInfo { get; }

        IntPtr NativePtr { get; }

        bool IsDeviceOpen { get; }
    }
}
