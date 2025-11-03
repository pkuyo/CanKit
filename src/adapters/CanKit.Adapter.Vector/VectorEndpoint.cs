using CanKit.Adapter.Vector.Definitions;
using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core;
using CanKit.Core.Endpoints;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Vector;

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
            var slash = path!.IndexOf('/')
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
        try
        {
            VxlApi.GetErrorString(0);
        }
        catch
        {
            /*Ignored*/
            yield break;
        }
        using (VectorDriver.Acquire())
        {
            for (int i = 0; ; i++)
            {
                uint hwIndex = 0;
                uint hwChannel = 0;
                uint hwType = 0;
                var st = VxlApi.xlGetApplConfig("CANoe", (uint)i, ref hwType, ref hwIndex,
                    ref hwChannel, VxlApi.XL_BUS_TYPE_CAN);
                if (st == VxlApi.XL_SUCCESS)
                {
                    var globalIndex = VxlApi.xlGetChannelIndex((int)hwType, (int)hwIndex, (int)hwChannel);
                    if (globalIndex >= 0 && VectorDriver.TryGetChannelInfo(globalIndex, out var info))
                    {
                        yield return new BusEndpointInfo()
                        {
                            Scheme = "vector",
                            DeviceType = VectorDeviceType.VectorXL,
                            Endpoint = $"vector://CANoe/{i}",
                            Title = info.Name + " (Vector)",
                            Meta = new Dictionary<string, string>
                            {
                                {"transceiver_name", info.TransceiverName ?? string.Empty},
                                {"channel_capabilities", info.ChannelCapabilities.ToString()},
                                {"channel_mask", info.ChannelMask.ToString()},
                                {"bus_type", info.ConnectedBusType.ToString()},
                                {"hardware_type", info.HardwareType.ToString()},
                                {"hardware_index", info.HardwareIndex.ToString()},
                                {"hardware_channel", info.HardwareChannel.ToString()},
                                {"gloabl_index", globalIndex.ToString()}
                            }
                        };
                    }
                }
                else
                {
                    yield break;
                }
            }
        }
    }
}
