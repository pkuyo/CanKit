using CanKit.Adapter.Vector.Definitions;
using System.Collections.Generic;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Vector;

[CanEndPoint("vector", ["vxl", "vectorxl"])]
internal static class VectorEndpoint
{
    public static PreparedBusContext Prepare(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        int channel = 0;
        string appName = string.Empty;

        var path = ep.Path?.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            //vector://[appName]/appChannel
            var slash = path.IndexOf('/')
                        >= 0 ? path.IndexOf('/') : path.IndexOf('\\');
            if (slash >= 0)
            {
                var left = path.Substring(0, slash).Trim();
                var right = path.Substring(slash + 1).Trim();
                if (!string.IsNullOrWhiteSpace(left)) appName = left;
                if (int.TryParse(right, out var idx)) channel = idx;
            }
            else
            {
                throw new ArgumentException("Invalid endpoint for Vector", nameof(path));
            }
        }

        var provider = CanRegistry.Registry.Resolve(VectorDeviceType.VectorXL);
        var (devOpt, devCfg) = provider.GetDeviceOptions();
        var (chOpt, chCfg) = provider.GetChannelOptions();
        chCfg.UseChannelName(appName);
        chCfg.UseChannelIndex(channel);
        configure?.Invoke(chCfg);
        return new PreparedBusContext(provider, devOpt, devCfg, chOpt, chCfg);
    }

    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        var (provider, devOpt, _, chOpt, chCfg) = Prepare(ep, configure);
        var device = provider.Factory.CreateDevice(devOpt);
        return CanBus.Open<VectorBus, VectorBusOptions, VectorBusInitConfigurator>(device, (VectorBusOptions)chOpt, (VectorBusInitConfigurator)chCfg);
    }

    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        yield break;
    }
}
