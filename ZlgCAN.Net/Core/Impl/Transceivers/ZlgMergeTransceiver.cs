using System.Collections.Generic;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Core.Transceivers;
using ZlgCAN.Net.Core.Utils;
using ZlgCAN.Net.Native;

namespace ZlgCAN.Net.Core.Impl.Transceivers
{

    public class ZlgMergeTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel channel, params CanTransmitData[] frames)
        {
            var transmitDataObj = ZlgNativeExtension.TransmitCanFrames(frames, (byte)channel.Options.ChannelIndex);
            return ZLGCAN.ZCAN_TransmitData(((ZlgCanChannel)channel).NativePtr,
                transmitDataObj,
                (uint)frames.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel channel, uint count = 1, int timeOut = -1)
        {
            ZLGCAN.ZCANDataObj[] zcanReceiveDataPtr = new ZLGCAN.ZCANDataObj[count];
            var recCount = ZLGCAN.ZCAN_ReceiveData(((ZlgCanChannel)channel).NativePtr, zcanReceiveDataPtr, count, timeOut);
            return ZlgNativeExtension.RecvCanFrames(zcanReceiveDataPtr, (int)recCount);
        }

        public ZlgFrameType FrameType => ZlgFrameType.Any;
    }
}