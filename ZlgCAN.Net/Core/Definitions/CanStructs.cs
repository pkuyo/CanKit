using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Native;
using static ZlgCAN.Net.Native.ZLGCAN;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit { }
}

namespace ZlgCAN.Net.Core.Definitions
{
    public readonly record struct CanChannelInfo(ICanDevice Device, uint ChannelIndex);

    public readonly record struct CanDeviceInfo(ZlgDeviceKind DeviceType, uint DeviceIndex);


    public record CanTransmitData
    {
        public CanFrameBase canFrame;
    }
    
    public record CanReceiveData
    {
        public CanFrameBase canFrame;
        public UInt64 timestamp;

        public DateTime Timestamp => DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).DateTime;
    }

    

}
