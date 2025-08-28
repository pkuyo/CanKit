using System;
using System.Collections.Generic;
using System.Text;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Models;

namespace ZlgCAN.Net.Core.Diagnostics
{
    public static class ZlgErr
    {

        public static void ThrowIfError(uint err)
        {
            if (err != 1)
                throw new Exception();
        }


        public static void ThrowIfNotSupport(this IChannelCapabilities channelCapabilities, CanFrameFlag filterFlag)
        {
            if (filterFlag == CanFrameFlag.Invalid)
                throw new InvalidOperationException();

            if (filterFlag == CanFrameFlag.Any)
                return;

            if ((channelCapabilities.SupportFlag & filterFlag) != filterFlag)
                throw new NotSupportedException();
        }
    }
}
