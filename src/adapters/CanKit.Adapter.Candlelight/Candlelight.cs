using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Core;

namespace CanKit.Adapter.Candlelight;

public static class Candlelight
{
    public static ICanBus Open(uint deviceIndex = 0, int channelIndex = 0, Action<IBusInitOptionsConfigurator>? configure = null)
        => CanBus.Open($"candlelight://{deviceIndex}/{channelIndex}", configure);
}
