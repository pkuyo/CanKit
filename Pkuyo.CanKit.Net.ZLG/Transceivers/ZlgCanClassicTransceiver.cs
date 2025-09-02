using System.Collections.Generic;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Utils;

namespace Pkuyo.CanKit.ZLG.Transceivers
{
    public class ZlgCanClassicTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, params CanTransmitData[] frames)
        {
            var zcanTransmitDatas = 
                frames.Select(i => i.canFrame)
                .OfType<CanClassicFrame>()
                .Select(i => i.ToTransmitData())
                .ToArray();

            return ZLGCAN.ZCAN_Transmit(((ZlgCanChannel)channel).NativeHandle, zcanTransmitDatas, (uint)zcanTransmitDatas.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1)
        {
            var data = new ZLGCAN.ZCAN_Receive_Data[count];

            var recCount = ZLGCAN.ZCAN_Receive(((ZlgCanChannel)channel).NativeHandle, data, count, timeOut);

            return data.Take((int)recCount).Select(i => new CanReceiveData()
            {
                timestamp = i.timestamp,
                canFrame = i.frame.FromReceiveData()
            });
        }

        public ZlgFrameType FrameType => ZlgFrameType.CanClassic;
    }
}