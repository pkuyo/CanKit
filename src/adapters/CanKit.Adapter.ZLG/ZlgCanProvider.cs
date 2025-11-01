using System.Collections.Generic;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Options;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG
{

    public abstract class ZlgCanProvider : ICanModelProvider, ICanCapabilityProvider
    {
        public virtual ZlgFeature ZlgFeature => ZlgFeature.None;
        public abstract DeviceType DeviceType { get; }

        public virtual CanFeature StaticFeatures => CanFeature.CanClassic |
                                                    CanFeature.ErrorCounters |
                                                    CanFeature.Echo;

        public ICanFactory Factory => CanRegistry.Registry.Factory("Zlg");

        public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
        {
            var option = new ZlgDeviceOptions(this);
            var cfg = new ZlgDeviceInitOptionsConfigurator();
            cfg.Init(option);
            return (option, cfg);
        }

        public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
        {
            var option = new ZlgBusOptions(this);
            var cfg = new ZlgBusInitConfigurator();
            cfg.Init(option);
            return (option, cfg);
        }

        public Capability QueryCapabilities(IBusOptions busOptions)
        {
            // Default ZLG pre-open sniff: use provider static features and provider-specific ZlgFeature.
            // Additional runtime limitations (like range/mask) are already represented by ZlgFeature.
            var custom = new Dictionary<string, object?>
            {
                { "zlg_features", ZlgFeature }
            };
            return new Capability(StaticFeatures, custom);
        }
    }
}
