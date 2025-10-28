using CanKit.Adapter.Vector.Definitions;
using CanKit.Adapter.Vector.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;
using System.Linq;
using CanKit.Adapter.Vector.Native;

namespace CanKit.Adapter.Vector;

public sealed class VectorProvider : ICanModelProvider, ICanCapabilityProvider
{
    public DeviceType DeviceType => VectorDeviceType.VectorXL;

    public CanFeature StaticFeatures => CanFeature.All;

    public ICanFactory Factory => CanRegistry.Registry.Factory("VECTOR");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new VectorBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            ChannelIndex = 0
        };
        var cfg = new VectorBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    internal VectorChannelInfo? QueryChannelInfo(IBusOptions busOptions)
    {
        var globalIndex = GlobalIndexAndAccessMask(busOptions.ChannelName, busOptions.ChannelIndex, out var mask);
        if (VectorDriver.TryGetChannelInfo(globalIndex, out var info))
        {
            info = info with
            {
                AppName = busOptions.ChannelName!,
                AppChannelIndex = busOptions.ChannelIndex
            };
        }

        return info;
    }

    public Capability QueryCapabilities(IBusOptions busOptions)
    {
        return QueryChannelInfo(busOptions)?.Capability ?? new Capability(busOptions.Features);
    }

    private static int GlobalIndexAndAccessMask(string? appName, int appChannelIndex, out uint accessMask)
    {
        accessMask = 0;
        if (appChannelIndex < 0) appChannelIndex = 0;
        if (string.IsNullOrWhiteSpace(appName)) return appChannelIndex;

        try
        {
            using (VectorDriver.Acquire())
            {
                uint hwType = 0, hwIndex = 0, hwChannel = 0;
                var st = VxlApi.xlGetApplConfig(appName!, (uint)appChannelIndex, ref accessMask, ref hwIndex, ref hwChannel, VxlApi.XL_BUS_TYPE_CAN);
                if (st == VxlApi.XL_SUCCESS)
                {
                    var globalIndex = VxlApi.xlGetChannelIndex((int)hwType, (int)hwIndex, (int)hwChannel);
                    if (globalIndex >= 0)
                        return globalIndex;
                }
            }
        }
        catch
        {
            // Fallback to direct index on any interop error
        }
        return appChannelIndex;
    }
}
