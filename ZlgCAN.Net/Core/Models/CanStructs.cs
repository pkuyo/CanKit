using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Native;
using static ZlgCAN.Net.Native.ZLGCAN;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal class IsExternalInit { }
}

namespace ZlgCAN.Net.Core.Models
{
    public readonly record struct CanChannelInfo(ICanDevice Device, uint ChannelIndex);

    public readonly record struct CanDeviceInfo(DeviceType DeviceType, uint DeviceIndex);


    public record CanReceiveData
    {
        public CanFrameBase canFrame;
        public UInt64 timestamp;

        public DateTime Timestamp => DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).DateTime;
    }

    public record CanChannelConfig
    {
        public uint ChannelIndex { get; set; }
    }
    public record USBCanChannelConfig : CanChannelConfig
    {
        public CanWorkMode Mode { get; set; } = CanWorkMode.Normal;

        public uint AcceptCode { get; set; } = 0x0;

        public uint AcceptMask { get; set; } = 0x1FFFFFFF;

    }

    public record ClassicCanChannelConfig : USBCanChannelConfig
    {
        public byte Timing0 { get; set; }

        public byte Timing1 { get; set; }

        public uint BaudRate { get; set; } = 500000;
    }

    public record FlexibleCanChannelConfig : USBCanChannelConfig
    {
        public byte ArbitrationBitTiming { get; set; }

        public byte DataBitTiming { get; set; }

        public byte Pad { get; set; }

        public uint ArbitrationBaudRate { get; set; } = 500000;

        public uint DataBaudRate { get; set; } = 2000000;
    }
    


}
