using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual;

[CanFactory("Virtual")]
public sealed class VirtualFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new NullDevice<NullDeviceOptions>(options);
    }

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver)
    {
        return new VirtualBus(options, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        return new VirtualTransceiver();
    }

    public bool Support(DeviceType deviceType) => true;
}
