// FAKE placeholder for PCAN-Basic exception type used by consumer code
#if FAKE
using System;
namespace Peak.Can.Basic
{
    public class PcanBasicException : Exception
    {
        public PcanBasicException() { }
        public PcanBasicException(string message) : base(message) { }
    }
}
#endif

