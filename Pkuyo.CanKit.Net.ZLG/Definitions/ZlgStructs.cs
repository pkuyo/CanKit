using System;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.ZLG.Definitions
{

    /// <summary>
    /// 周立功 CAN 监听器错误信息的默认实现。
    /// </summary>
    public record ZlgErrorInfo : ICanErrorInfo
    {
        /// <summary>
        /// 初始化错误信息并指定原始错误码。
        /// </summary>
        public ZlgErrorInfo(uint rawErrorCode)
        {
            RawErrorCode = rawErrorCode;
        }
        /// <inheritdoc />
        public FrameErrorKind Kind { get; init; }
        /// <inheritdoc />
        public DateTime SystemTimestamp { get; init; }
        /// <inheritdoc />
        public uint RawErrorCode { get; init; }

        /// <summary>
        /// 将原始错误码转换为中联定义的标志位枚举。
        /// </summary>
        public ZlgErrorFlag ErrorCode => (ZlgErrorFlag)RawErrorCode;

        /// <inheritdoc />
        public ulong? TimeOffset { get; init; }
        /// <inheritdoc />
        public FrameDirection Direction { get; init; }
        /// <inheritdoc />
        public ICanFrame Frame { get; init; }
    }
}