using System;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Abstractions.API.Transport;

public interface IIsoTpOptions
{
    IsoTpEndpoint Endpoint { get; }
    CanFeature Features { get; set; }
    Capability Capabilities { get; set; }
    int ChannelIndex { get; }
    string? ChannelName { get; }
    CanBusTiming BitTiming { get; }
    bool InternalResistance { get; }
    CanProtocolMode ProtocolMode { get; }
    CanFeature EnabledSoftwareFallback { get; }
    IBufferAllocator BufferAllocator { get; }


    /* ----IsoTpSettings----- */

    bool CanPadding { get; }
    TimeSpan? GlobalBusGuard { get; }
    bool N_AxCheck { get; }

    TimeSpan N_As { get; }
    TimeSpan N_Ar { get; }
    TimeSpan N_Bs { get; }
    TimeSpan N_Br { get; }
    TimeSpan N_Cs { get; }
    TimeSpan N_Cr { get; }

    int MaxFrameLength { get; }
}
