using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ControlCAN.Utils;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ControlCAN;

internal sealed class ControlCanTransceiver : ITransceiver
{
    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<CanFrame> frames)
    {
        if (bus is not ControlCanBus ctl)
            throw new ArgumentException("bus type mismatch");
        var pObj = stackalloc CcApi.VCI_CAN_OBJ[CcApi.BATCH_COUNT];
        var retry = bus.Options.TxRetryPolicy == TxRetryPolicy.AlwaysRetry;
        int index = 0;
        var written = 0U;
        foreach (var f in frames)
        {
            if (f.FrameKind is not CanFrameType.Can20)
                throw new NotSupportedException("ControlCAN only supports Classical CAN frames.");
            f.ToNative(&pObj[index++], retry);
            if (index == CcApi.BATCH_COUNT)
            {
                written += CcApi.VCI_Transmit(ctl.RawDevType, ctl.DevIndex, ctl.CanIndex, pObj, CcApi.BATCH_COUNT);
                if (written != CcApi.BATCH_COUNT)
                    return (int)written;
                index = 0;
            }
        }

        if (index != 0)
        {
            written += CcApi.VCI_Transmit(ctl.RawDevType, ctl.DevIndex, ctl.CanIndex, pObj, (uint)index);
        }
        return (int)written;
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<CanFrame> frames)
    {
        if (bus is not ControlCanBus ctl)
            throw new ArgumentException("bus type mismatch");
        var pObj = stackalloc CcApi.VCI_CAN_OBJ[CcApi.BATCH_COUNT];
        var retry = bus.Options.TxRetryPolicy == TxRetryPolicy.AlwaysRetry;
        int index = 0;
        var written = 0U;
        foreach (var f in frames)
        {
            if (f.FrameKind is not CanFrameType.Can20)
                throw new NotSupportedException("ControlCAN only supports Classical CAN frames.");
            f.ToNative(&pObj[index++], retry);
            if (index == CcApi.BATCH_COUNT)
            {
                written += CcApi.VCI_Transmit(ctl.RawDevType, ctl.DevIndex, ctl.CanIndex, pObj, CcApi.BATCH_COUNT);
                if (written != CcApi.BATCH_COUNT)
                    return (int)written;
                index = 0;
            }
        }

        if (index != 0)
        {
            written += CcApi.VCI_Transmit(ctl.RawDevType, ctl.DevIndex, ctl.CanIndex, pObj, (uint)index);
        }
        return (int)written;
    }

    public unsafe int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frames)
    {
        if (bus is not ControlCanBus ctl)
            throw new ArgumentException("bus type mismatch");
        if (frames.FrameKind is not CanFrameType.Can20)
            throw new NotSupportedException("ControlCAN only supports Classical CAN frames.");
        var pObj = stackalloc CcApi.VCI_CAN_OBJ[1];
        var retry = bus.Options.TxRetryPolicy == TxRetryPolicy.AlwaysRetry;
        frames.ToNative(&pObj[0], retry);
        return (int)CcApi.VCI_Transmit(ctl.RawDevType, ctl.DevIndex, ctl.CanIndex, pObj, 1);
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
    {
        if (bus is not ControlCanBus ctl)
            throw new ArgumentException("bus type mismatch");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var cap = Math.Max(1, Math.Min(count, CcApi.BATCH_COUNT));
        var arr = new CcApi.VCI_CAN_OBJ[cap];
        while (count > 0)
        {
            var recCount = Math.Min(cap, count);
            var got = CcApi.VCI_Receive(ctl.RawDevType, ctl.DevIndex, ctl.CanIndex, arr, (uint)recCount, timeOut);
            for (int i = 0; i < got; i++)
            {
                var obj = arr[i];
                var frame = obj.FromNative(bus.Options.BufferAllocator);
                yield return new CanReceiveData(frame)
                {
                    ReceiveTimestamp = TimeSpan.FromTicks(obj.TimeStamp * TimeSpan.TicksPerMillisecond / 10),
                };
            }
            count -= (int)got;
            if (got != recCount)
                yield break;
        }
    }
}

