using System;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Attributes
{
    public class CanFrameAttribute(CanFrameType frameType) : Attribute
    {
        public CanFrameType FrameType { get; } = frameType;
    }
}