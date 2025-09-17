using System;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.ZLG.Definitions
{

    
    public record ZlgErrorInfo : ICanErrorInfo
    {
        public ZlgErrorInfo(uint rawErrorCode)
        {
            RawErrorCode = rawErrorCode;
        }
        public FrameErrorKind Kind { get; init; }
        public DateTime SystemTimestamp { get; init; }
        public uint RawErrorCode { get; init; }

        public ZlgErrorFlag ErrorCode => (ZlgErrorFlag)RawErrorCode;
        
        public ulong? TimeOffset { get; init; }
        public FrameDirection Direction { get; init; }
        public ICanFrame Frame { get; init; }
    }
}