using System.Collections.Generic;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Transceivers
{

    public sealed class ZlgMergeTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel,
            IEnumerable<CanTransmitData> frames, int _ = 0)
        {
            var transmitDataObj = ZlgNativeExtension.TransmitCanFrames(frames, (byte)channel.Options.ChannelIndex);
            return ZLGCAN.ZCAN_TransmitData(((ZlgCanBus)channel).NativeHandle.DeviceHandle,
                transmitDataObj,
                (uint)transmitDataObj.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
        {
            ZLGCAN.ZCANDataObj[] zcanReceiveDataPtr = new ZLGCAN.ZCANDataObj[count];
            var recCount = ZLGCAN.ZCAN_ReceiveData(((ZlgCanBus)channel).NativeHandle.DeviceHandle, zcanReceiveDataPtr, count, timeOut);
            return ZlgNativeExtension.RecvCanFrames(zcanReceiveDataPtr, (int)recCount);
        }

        public ZlgFrameType FrameType => ZlgFrameType.Any;
    }

}
