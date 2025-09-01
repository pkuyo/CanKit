using System;

namespace Pkuyo.CanKit.Net.Core.Diagnostics
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
