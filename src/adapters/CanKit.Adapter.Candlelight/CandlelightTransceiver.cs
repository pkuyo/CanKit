using System;
using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Adapter.Candlelight;

internal sealed class CandlelightTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<CanFrame> frames)
    {
        var candle = (CandlelightBus)bus;
        var count = 0;
        foreach (var frame in frames) { candle.Send(frame); count++; }
        return count;
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, ReadOnlySpan<CanFrame> frames)
    {
        var candle = (CandlelightBus)bus;
        foreach (var frame in frames) candle.Send(frame);
        return frames.Length;
    }

    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, in CanFrame frame)
    {
        ((CandlelightBus)bus).Send(frame);
        return 1;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int timeOut = 0)
        => bus.Receive(count, timeOut);
}
