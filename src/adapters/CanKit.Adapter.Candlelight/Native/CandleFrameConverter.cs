using System;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Adapter.Candlelight.Native;

internal static class CandleFrameConverter
{
    internal static CandleNative.Frame ToNative(in CanFrame frame, byte channel)
    {
        if (frame.FrameKind != CanFrameType.Can20 || frame.Len > 8)
            throw new NotSupportedException("Candle.dll supports only classic CAN frames with up to eight bytes.");
        if (frame.IsErrorFrame)
            throw new NotSupportedException("Sending CAN error frames is not supported.");

        var native = new CandleNative.Frame
        {
            CanId = (uint)frame.ID,
            Channel = channel,
            Dlc = (byte)frame.Len,
            Data = new byte[8]
        };
        if (frame.IsExtendedFrame) native.CanId |= CandleNative.ExtendedId;
        if (frame.IsRemoteFrame) native.CanId |= CandleNative.RemoteRequest;
        frame.Data.Span.CopyTo(native.Data);
        return native;
    }

    internal static CanFrame FromNative(in CandleNative.Frame native)
    {
        if (native.Dlc > 8 || native.Data == null || native.Data.Length < native.Dlc)
            throw new InvalidOperationException("The Candle device returned an invalid classic CAN frame.");
        var extended = (native.CanId & CandleNative.ExtendedId) != 0;
        var remote = (native.CanId & CandleNative.RemoteRequest) != 0;
        var error = (native.CanId & CandleNative.ErrorId) != 0;
        var id = (int)(native.CanId & (extended || error ? 0x1FFFFFFFu : 0x7FFu));
        var data = new byte[remote ? 0 : native.Dlc];
        Array.Copy(native.Data, data, data.Length);
        return CanFrame.Classic(id, data, extended, remote, error);
    }
}
