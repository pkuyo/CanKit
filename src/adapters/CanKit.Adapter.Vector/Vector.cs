using System;
using CanKit.Adapter.Vector.Definitions;
using CanKit.Core;

namespace CanKit.Adapter.Vector;

public static class Vector
{
    /// <summary>
    /// Open a Vector XL channel by channel index.
    /// </summary>
    public static VectorBus Open(int channel, Action<VectorBusInitConfigurator>? configure = null)
    {
        return CanBus.Open<VectorBus, VectorBusOptions, VectorBusInitConfigurator>(
            VectorDeviceType.VectorXL,
            cfg =>
            {
                cfg.UseChannelIndex(channel);
                configure?.Invoke(cfg);
            });
    }
}

