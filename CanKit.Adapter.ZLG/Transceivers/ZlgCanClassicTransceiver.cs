using System.Collections.Generic;
using System.Linq;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Transceivers
{
    public sealed class ZlgCanClassicTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel,
            IEnumerable<CanTransmitData> frames, int _ = 0)
        {

            var zcanTransmitDatas =
                frames.Select(i => i.CanFrame)
                .OfType<CanClassicFrame>()
                .Select(i => i.ToTransmitData())
                .ToArray();

            return ZLGCAN.ZCAN_Transmit(((ZlgCanBus)channel).NativeHandle, zcanTransmitDatas, (uint)zcanTransmitDatas.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
        {
            var data = new ZLGCAN.ZCAN_Receive_Data[count];

            var recCount = ZLGCAN.ZCAN_Receive(((ZlgCanBus)channel).NativeHandle, data, count, timeOut);

            return data.Take((int)recCount).Select(i => new CanReceiveData(i.frame.FromReceiveData())
            {
                recvTimestamp = i.timestamp
            });
        }

        public ZlgFrameType FrameType => ZlgFrameType.CanClassic;
    }
}
