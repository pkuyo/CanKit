namespace ZlgCAN.Net.Core.Definitions
{
    public record CanChannelConfig
    {
        public uint ChannelIndex { get; init; }
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