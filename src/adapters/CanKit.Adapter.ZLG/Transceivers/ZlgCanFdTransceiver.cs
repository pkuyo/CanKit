using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Transceivers
{
    public sealed class ZlgCanFdTransceiver : ITransceiver
    {
        public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus,
            IEnumerable<CanFrame> frames)
        {
            unsafe
            {
                var transmitData = stackalloc ZLGCAN.ZCAN_TransmitFD_Data[ZLGCAN.BATCH_COUNT];
                var index = 0;
                int sent = 0;
                var echo = bus.Options.WorkMode == ChannelWorkMode.Echo;
                foreach (var f in frames)
                {
                    if (index == ZLGCAN.BATCH_COUNT)
                    {
                        var re = ZLGCAN.ZCAN_TransmitFD(((ZlgCanBus)bus).Handle, transmitData, ZLGCAN.BATCH_COUNT);
                        index = 0;
                        if (re != ZLGCAN.BATCH_COUNT)
                            return sent;
                        sent += ZLGCAN.BATCH_COUNT;
                    }
                    if (f.FrameKind is not CanFrameType.CanFd)
                        continue;
                    f.ToTransmitFdData(echo, transmitData, index);
                    index++;
                }
                return (int)(sent + ZLGCAN.ZCAN_TransmitFD(((ZlgCanBus)bus).Handle, transmitData, (uint)index));
            }

        }
        public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus,
            ReadOnlySpan<CanFrame> frames)
        {
            unsafe
            {
                var transmitData = stackalloc ZLGCAN.ZCAN_TransmitFD_Data[ZLGCAN.BATCH_COUNT];
                var index = 0;
                int sent = 0;
                var echo = bus.Options.WorkMode == ChannelWorkMode.Echo;
                foreach (var f in frames)
                {
                    if (index == ZLGCAN.BATCH_COUNT)
                    {
                        var re = ZLGCAN.ZCAN_TransmitFD(((ZlgCanBus)bus).Handle, transmitData, ZLGCAN.BATCH_COUNT);
                        index = 0;
                        if (re != ZLGCAN.BATCH_COUNT)
                            return sent;
                        sent += ZLGCAN.BATCH_COUNT;
                    }
                    if (f.FrameKind is not CanFrameType.CanFd)
                        continue;
                    f.ToTransmitFdData(echo, transmitData, index);
                    index++;
                }
                return (int)(sent + ZLGCAN.ZCAN_TransmitFD(((ZlgCanBus)bus).Handle, transmitData, (uint)index));
            }
        }

        public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frame)
        {
            unsafe
            {
                var transmitData = stackalloc ZLGCAN.ZCAN_TransmitFD_Data[1];
                var echo = bus.Options.WorkMode == ChannelWorkMode.Echo;

                if (frame.FrameKind is not CanFrameType.CanFd)
                {
                    throw new InvalidOperationException("Zlg Fd transceiver requires CanFdFrame.");
                }

                frame.ToTransmitFdData(echo, transmitData, 0);
                return (int)ZLGCAN.ZCAN_TransmitFD(((ZlgCanBus)bus).Handle, transmitData, 1);
            }
        }

        public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
        {
            var pool = ArrayPool<ZLGCAN.ZCAN_ReceiveFD_Data>.Shared;
            var buf = pool.Rent(ZLGCAN.BATCH_COUNT);
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var startTime = Environment.TickCount;
            try
            {
                while (count > 0)
                {
                    var remaining = timeOut - Environment.TickCount + startTime;
                    if (timeOut == -1)
                        remaining = -1;
                    var recCount = ZLGCAN.ZCAN_ReceiveFD(((ZlgCanBus)bus).Handle, buf, (uint)count, remaining);
                    if (recCount == 0)
                        yield break;
                    for (int i = 0; i < recCount; i++)
                    {
                        yield return new CanReceiveData(buf[i].frame.FromReceiveData(bus.Options.BufferAllocator))
                        {
                            // ZLG timestamp is in microseconds
                            ReceiveTimestamp = TimeSpan.FromTicks((long)buf[i].timestamp * 10)
                        };
                    }
                    count -= (int)recCount;
                }
            }
            finally
            {
                pool.Return(buf);
            }
        }
    }
}
