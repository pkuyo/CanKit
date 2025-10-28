using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ControlCAN.Diagnostics;

/// <summary>
/// Exception for ControlCAN native call failures (ControlCAN 原生调用失败异常)。
/// </summary>
public sealed class ControlCanException : CanNativeCallException
{
    public ControlCanException(string operation, string message, uint statusCode,
        ICanErrorInfo? channelErrorInfo = null)
        : base(operation, message, statusCode)
    {
        StatusCode = statusCode;
        ChannelErrorInfo = channelErrorInfo;
    }


    public uint StatusCode { get; }

    public ICanErrorInfo? ChannelErrorInfo { get; }
}

