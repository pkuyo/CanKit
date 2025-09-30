using System.Collections.Generic;
using System.Linq;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Transceivers
{
    public sealed class ZlgCanFdTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel,
            IEnumerable<CanTransmitData> frames, int _ = 0)
        {
            var zcanTransmitData =
                frames.Select(i => i.CanFrame)
                    .OfType<CanFdFrame>()
                    .Select(i => i.ToTransmitData())
                    .ToArray();

            return ZLGCAN.ZCAN_TransmitFD(((ZlgCanBus)channel).NativeHandle, zcanTransmitData, (uint)zcanTransmitData.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
        {
            var data = new ZLGCAN.ZCAN_ReceiveFD_Data[count];

            var recCount = ZLGCAN.ZCAN_ReceiveFD(((ZlgCanBus)channel).NativeHandle, data, count, timeOut);

            return data.Take((int)recCount).Select(i => new CanReceiveData(i.frame.FromReceiveData())
            {
                RecvTimestamp = i.timestamp
            });
        }


        public ZlgFrameType FrameType => ZlgFrameType.CanFd;
    }
}
