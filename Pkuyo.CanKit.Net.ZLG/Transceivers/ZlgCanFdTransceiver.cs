using System.Collections.Generic;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Native;
using Pkuyo.CanKit.ZLG.Utils;

namespace Pkuyo.CanKit.ZLG.Transceivers
{
    public sealed class ZlgCanFdTransceiver : IZlgTransceiver
    {
        public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel,
            params IEnumerable<CanTransmitData> frames)
        {
            var zcanTransmitData = 
                frames.Select(i => i.CanFrame)
                    .OfType<CanFdFrame>()
                    .Select(i => i.ToTransmitData())
                    .ToArray();

            return ZLGCAN.ZCAN_TransmitFD(((ZlgCanChannel)channel).NativeHandle, zcanTransmitData, (uint)zcanTransmitData.Length);
        }

        public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1)
        {
            var data = new ZLGCAN.ZCAN_ReceiveFD_Data[count];

            var recCount = ZLGCAN.ZCAN_ReceiveFD(((ZlgCanChannel)channel).NativeHandle, data, count, timeOut);

            return data.Take((int)recCount).Select(i => new CanReceiveData(i.frame.FromReceiveData())
            {
                recvTimestamp = i.timestamp
            });
        }


        public ZlgFrameType FrameType => ZlgFrameType.CanFd;
    }
}