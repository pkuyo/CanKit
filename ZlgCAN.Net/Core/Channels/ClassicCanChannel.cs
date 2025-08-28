using System;
using System.Collections.Generic;
using System.Text;
using ZlgCAN.Net.Core.Models;

namespace ZlgCAN.Net.Core.Channels
{
    internal class ClassicCanChannel : CanChannel
    {
        public ClassicCanChannel(IntPtr nativePtr, CanChannelConfig config) : base(nativePtr, config)
        {
        }

        public override CanFrameFlag SupportFlag => CanFrameFlag.ClassicCan;
    }
}
