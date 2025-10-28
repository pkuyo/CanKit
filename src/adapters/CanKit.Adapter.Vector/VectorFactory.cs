using CanKit.Adapter.Vector.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.Vector;

[CanFactory("VECTOR")]
public sealed class VectorFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        return new NullDevice<NullDeviceOptions>(options);
    }

    public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        return new VectorBus(options, transceiver, provider);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
    {
        return busOptions.ProtocolMode switch
        {
            CanProtocolMode.CanFd => new Transceivers.VectorFdTransceiver(),
            _ => new Transceivers.VectorClassicTransceiver(),
        };
    }

    public bool Support(DeviceType deviceType) => deviceType.Equals(VectorDeviceType.VectorXL);
}

