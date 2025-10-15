using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CanKit.Adapter.ZLG.Native;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Transceivers;

public class ZlgCanMergeTransceiver : ITransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus,
        IEnumerable<ICanFrame> frames, int _)
    {
        var canBus = (ZlgCanBus)bus;
        var buf = stackalloc ZLGCAN.ZCANDataObj[ZLGCAN.BATCH_COUNT];
        var index = 0;
        var sent = 0;
        foreach (var frame in frames)
        {
            if (index == ZLGCAN.BATCH_COUNT)
            {
                sent += (int)ZLGCAN.ZCAN_TransmitData(canBus.Handle.DeviceHandle, buf, ZLGCAN.BATCH_COUNT);
                index = 0;
            }
            frame.ToZCANObj(canBus, &buf[index]);
            index++;
        }
        return sent + (int)ZLGCAN.ZCAN_TransmitData(canBus.Handle.DeviceHandle, buf, (uint)index);
    }


    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        var canBus = (ZlgCanBus)bus;
        var pool = ArrayPool<ZLGCAN.ZCANDataObj>.Shared;
        var buf = pool.Rent(ZLGCAN.BATCH_COUNT);
        try
        {
            var startTick = Environment.TickCount;
            while (count > 0)
            {
                var remaining = timeOut < 0 ? -1 : timeOut - Environment.TickCount + startTick;
                var rec = 0;
                rec = (int)ZLGCAN.ZCAN_ReceiveData(canBus.Handle.DeviceHandle, buf,
                    Math.Min(ZLGCAN.BATCH_COUNT, (uint)count), remaining);

                count -= rec;
                for (int i = 0; i < rec; i++)
                {
                    yield return ZlgUtils.FromZCANData(in buf[i]);
                }
            }
        }
        finally
        {
            pool.Return(buf);
        }

    }
}
