using System;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Abstractions.API.Transport;

/// <summary>
/// IsoTP bus initialization configurator interface.
/// </summary>
public interface IIsoTpInitConfigurator
{
    // Properties
    int ChannelIndex { get; }
    string? ChannelName { get; }
    CanBusTiming BitTiming { get; }
    bool InternalResistance { get; }
    CanProtocolMode ProtocolMode { get; }
    int AsyncBufferCapacity { get; }

    // IsoTP specific setters (fluent)
    IIsoTpInitConfigurator CanPadding(bool padding);
    IIsoTpInitConfigurator GlobalBusGuard(TimeSpan globalBusGuard);
    IIsoTpInitConfigurator MaxFrameLength(int maxFrameLength);

    IIsoTpInitConfigurator N_As(TimeSpan n_As);
    IIsoTpInitConfigurator N_Ar(TimeSpan n_Ar);
    IIsoTpInitConfigurator N_Bs(TimeSpan n_Bs);
    IIsoTpInitConfigurator N_Br(TimeSpan n_Br);
    IIsoTpInitConfigurator N_Cs(TimeSpan n_Cs);
    IIsoTpInitConfigurator N_Cr(TimeSpan n_Cr);

    // Bus selection and base options
    IIsoTpInitConfigurator UseChannelIndex(int index);
    IIsoTpInitConfigurator UseChannelName(string name);

    IIsoTpInitConfigurator Baud(int baud,
        uint? clockMHz = null,
        ushort? samplePointPermille = null);

    IIsoTpInitConfigurator Fd(int abit, int dbit,
        uint? clockMHz = null,
        ushort? nominalSamplePointPermille = null,
        ushort? dataSamplePointPermille = null);

    IIsoTpInitConfigurator TimingClassic(CanClassicTiming timing);
    IIsoTpInitConfigurator TimingFd(CanFdTiming timing);
    IIsoTpInitConfigurator InternalRes(bool enabled);
    IIsoTpInitConfigurator SetProtocolMode(CanProtocolMode mode);
    IIsoTpInitConfigurator SetAsyncBufferCapacity(int capacity);
    IIsoTpInitConfigurator BufferAllocator(IBufferAllocator bufferAllocator);

    // Custom extension
    IIsoTpInitConfigurator Custom(string key, object value);
}

