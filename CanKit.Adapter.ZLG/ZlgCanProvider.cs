using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Options;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Registry;

namespace CanKit.Adapter.ZLG
{

    public abstract class ZlgCanProvider : ICanModelProvider
    {
        public virtual ZlgFeature ZlgFeature => ZlgFeature.None;
        public abstract DeviceType DeviceType { get; }

        public virtual CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.Filters | CanFeature.ErrorCounters;

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
    }
}
