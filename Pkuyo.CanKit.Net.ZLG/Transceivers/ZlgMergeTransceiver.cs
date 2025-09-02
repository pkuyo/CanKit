using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Options;
using Pkuyo.CanKit.ZLG.Utils;

namespace Pkuyo.CanKit.ZLG.Transceivers
{
    
    public class ZlgMergeTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, params CanTransmitData[] frames)
        {
            var transmitDataObj = ZlgNativeExtension.TransmitCanFrames(frames, (byte)channel.Options.ChannelIndex);
            return ZLGCAN.ZCAN_TransmitData(((ZlgCanChannel)channel).NativeHandle.DeviceHandle,
                transmitDataObj,
                (uint)frames.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1)
        {
            ZLGCAN.ZCANDataObj[] zcanReceiveDataPtr = new ZLGCAN.ZCANDataObj[count];
            var recCount = ZLGCAN.ZCAN_ReceiveData(((ZlgCanChannel)channel).NativeHandle.DeviceHandle, zcanReceiveDataPtr, count, timeOut);
            return ZlgNativeExtension.RecvCanFrames(zcanReceiveDataPtr, (int)recCount);
        }

        public ZlgFrameType FrameType => ZlgFrameType.Any;
    }
    
}