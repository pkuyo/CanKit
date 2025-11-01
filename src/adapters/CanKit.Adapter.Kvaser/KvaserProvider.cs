using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;
using CanKit.Adapter.Kvaser.Native;

namespace CanKit.Adapter.Kvaser;

public sealed class KvaserProvider : ICanModelProvider, ICanCapabilityProvider
{
    public DeviceType DeviceType => KvaserDeviceType.CANlib;

    public CanFeature StaticFeatures => CanFeature.All;

    public ICanFactory Factory => CanRegistry.Registry.Factory("KVASER");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new KvaserBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal
        };
        var cfg = new KvaserBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    internal static int GetChannelIndex(IBusOptions opt)
    {
        if (!string.IsNullOrWhiteSpace(opt.ChannelName))
        {
            // Enumerate channels to match by name
            int n = 0;
            if (Canlib.canGetNumberOfChannels(out n) == Canlib.canStatus.canOK)
            {
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        Canlib.GetChannelName(i, out var name);
                        if (!string.IsNullOrWhiteSpace(name) &&
                            string.Equals(name, opt.ChannelName, StringComparison.OrdinalIgnoreCase))
                        {
                            return i;
                        }
                    }
                    catch { }

                }
            }
        }

        return opt.ChannelIndex;
    }

    public Capability QueryCapabilities(IBusOptions busOptions)
    {
        // Pre-open capability probe via CANlib channel data
        int ch = GetChannelIndex(busOptions);
        try { Canlib.canInitializeLibrary(); } catch { }
        CanFeature features = CanFeature.CanClassic | CanFeature.MaskFilter | CanFeature.Echo | CanFeature.ErrorFrame;
        var st = Canlib.GetUInt32(ch, Canlib.canCHANNELDATA_CHANNEL_CAP, out var caps);
        if (st == Canlib.canStatus.canOK)
        {
            if ((caps & Canlib.canCHANNEL_CAP_CAN_FD) != 0 ||
                (caps & Canlib.canCHANNEL_CAP_CAN_FD_NONISO) != 0)
                features |= CanFeature.CanFd;

            if ((caps & Canlib.canCHANNEL_CAP_SILENT_MODE) != 0)
                features |= CanFeature.ListenOnly;

            if ((caps & Canlib.canCHANNEL_CAP_ERROR_COUNTERS) != 0)
                features |= CanFeature.ErrorCounters;

            if ((caps & Canlib.canCHANNEL_CAP_BUS_STATISTICS) != 0)
                features |= CanFeature.BusUsage;
        }
        // custom payload: raw channel caps
        var custom = new Dictionary<string, object?>
        {
            { "kv_caps", st == Canlib.canStatus.canOK ? caps : 0u }
        };
        return new Capability(features, custom);
    }
}
