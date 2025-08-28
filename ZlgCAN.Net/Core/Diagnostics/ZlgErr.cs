using System;
using System.Collections.Generic;
using System.Text;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Diagnostics
{
    public static class ZlgErr
    {

        public static void ThrowIfError(uint err)
        {
            if (err != 1)
                throw new Exception();
        }
    }
}
