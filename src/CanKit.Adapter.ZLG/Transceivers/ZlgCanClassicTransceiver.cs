using System;
using System.Buffers;
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
            unsafe
            {
                var transmitData = stackalloc ZLGCAN.ZCAN_Transmit_Data[ZLGCAN.BATCH_COUNT];
                var index = 0;
                long sent = 0;
                var echo = channel.Options.WorkMode == ChannelWorkMode.Echo;
                foreach (var f in frames)
                {
                    if (index == ZLGCAN.BATCH_COUNT)
                    {
                        var re = ZLGCAN.ZCAN_Transmit(((ZlgCanBus)channel).NativeHandle, transmitData, ZLGCAN.BATCH_COUNT);
                        index = 0;
                        if (re != ZLGCAN.BATCH_COUNT)
                            return (uint)sent;
                        sent += ZLGCAN.BATCH_COUNT;
                    }
                    if(f.CanFrame is not CanClassicFrame cf)
                        continue;
                    cf.ToTransmitData(echo, transmitData, index);
                    index++;
                }
                return (uint)(sent + ZLGCAN.ZCAN_Transmit(((ZlgCanBus)channel).NativeHandle, transmitData, (uint)index));
            }

        }

        public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, uint count = 1, int timeOut = 0)
        {
            var pool = ArrayPool<ZLGCAN.ZCAN_Receive_Data>.Shared;
            var buf = pool.Rent(ZLGCAN.BATCH_COUNT);
            try
            {
                while (count > 0)
                {

                    var recCount = ZLGCAN.ZCAN_Receive(((ZlgCanBus)bus).NativeHandle, buf, count, timeOut);
                    if(recCount == 0)
                        yield break;
                    for (int i = 0; i < recCount; i++)
                    {
                        yield return new CanReceiveData(buf[i].frame.FromReceiveData())
                        {
                            // ZLG timestamp is in microseconds
                            ReceiveTimestamp = TimeSpan.FromTicks((long)buf[i].timestamp * 10)
                        };
                    }
                    count -= recCount;
                }
            }
            finally
            {
                pool.Return(buf);
            }
        }

        public ZlgFrameType FrameType => ZlgFrameType.CanClassic;
    }
}
