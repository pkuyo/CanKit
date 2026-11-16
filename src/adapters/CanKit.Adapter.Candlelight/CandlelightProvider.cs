using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Candlelight;

public static class CandlelightDeviceType
{
    public static readonly DeviceType Candlelight = DeviceType.Register("Candlelight.USB-CAN");
}

public sealed class CandlelightProvider : ICanModelProvider
{
    public DeviceType DeviceType => CandlelightDeviceType.Candlelight;
    public CanFeature StaticFeatures => CanFeature.CanClassic;
    public ICanFactory Factory => CanRegistry.Registry.Factory("Candlelight");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new CandlelightDeviceOptions(this);
        var configurator = new CandlelightDeviceInitConfigurator();
        configurator.Init(options);
        return (options, configurator);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new CandlelightBusOptions(this);
        var configurator = new CandlelightBusInitConfigurator();
        configurator.Init(options);
        configurator.UpdateSoftwareFeatures(options.EnabledSoftwareFallback);
        return (options, configurator);
    }
}
