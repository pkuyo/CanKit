using System.Collections.Generic;
using System.Linq;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Core.Transceivers;
using ZlgCAN.Net.Native;

namespace ZlgCAN.Net.Core.Impl.Transceivers
{
    public class ZlgCanClassicTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel channel, params CanTransmitData[] frames)
        {
            var zcanTransmitDatas = frames.OfType<CanClassicFrame>().Select(i => i.ToTransmitData()).ToArray();

            return ZLGCAN.ZCAN_Transmit(((ZlgCanChannel)channel).NativePtr, zcanTransmitDatas, (uint)zcanTransmitDatas.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel channel, uint count = 1, int timeOut = -1)
        {
            var data = new ZLGCAN.ZCAN_Receive_Data[count];

            var recCount = ZLGCAN.ZCAN_Receive(((ZlgCanChannel)channel).NativePtr, data, count, timeOut);

            return data.Take((int)recCount).Select(i => new CanReceiveData()
            {
                timestamp = i.timestamp,
                canFrame = CanClassicFrame.FromReceiveData(i.frame)
            });
        }

        public ZlgFrameType FrameType => ZlgFrameType.CanClassic;
    }
}