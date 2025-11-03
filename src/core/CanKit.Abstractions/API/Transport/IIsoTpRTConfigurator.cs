using System;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Abstractions.API.Transport;

public interface IIsoTpRTConfigurator
{
    IsoTpEndpoint Endpoint { get; }
    public bool CanPadding { get; }
    public TimeSpan? GlobalBusGuard { get; }
    public bool N_AxCheck { get; }

    public TimeSpan N_As { get; }
    public TimeSpan N_Ar { get; }
    public TimeSpan N_Bs { get; }
    public TimeSpan N_Br { get; }
    public TimeSpan N_Cs { get; }
    public TimeSpan N_Cr { get; }

    public int MaxFrameLength { get; }
    public int ChannelIndex { get; }
    public string? ChannelName { get; }
    public CanBusTiming BitTiming { get; }

    CanFeature Features { get; }
    Capability Capabilities { get; }

    public bool InternalResistance { get; }
    public CanProtocolMode ProtocolMode { get; }
    public int AsyncBufferCapacity { get; }
    public IBufferAllocator BufferAllocator { get; }
}
