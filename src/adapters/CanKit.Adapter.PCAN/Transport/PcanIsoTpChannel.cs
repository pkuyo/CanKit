using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Adapter.PCAN.Native;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Transport;

public class PcanIsoTpChannel : IIsoTpChannel
{
    public PcanIsoTpChannel(IIsoTpOptions options, IIsoTpRTConfigurator cfg)
    {
        Options = cfg;
        _busOptions = (IBusRTOptionsConfigurator)cfg;
        _handle = PcanProvider.ParseHandle(Options.ChannelName!);
        NativeHandle = new BusNativeHandle((int)_handle);
        options.Capabilities = PcanProvider.QueryCapabilities(_handle, Options.Features);
        options.Features = options.Capabilities.Features;
        try
        {
            if (Api.GetValue(_handle, PcanParameter.ChannelCondition, out uint raw) == PcanStatus.OK)
            {
                var cond = (ChannelCondition)raw;
                if ((cond & ChannelCondition.ChannelAvailable) != ChannelCondition.ChannelAvailable)
                    throw new CanBusCreationException("PCAN handle is not available");
            }
            else
            {
                CanKitLogger.LogWarning("PCAN can't get channel condition for handle");
            }
        }
        catch (PcanBasicException)
        {
            throw new CanBusCreationException("PCAN handle is invalid");
        }

        if (Options.ProtocolMode == CanProtocolMode.CanFd)
        {

            //CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.CanFd);

            var fd = PcanUtils.MapFdBitrate(Options.BitTiming);
            var st = PcanIsoTp.InitializeFd(_handle, fd);
            if (st != PcanIsoTp.PCanTpStatus.Ok)
            {
                throw new CanBusCreationException($"PCAN InitializeFD failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: InitializeFD succeeded.");
        }
        else if (Options.ProtocolMode == CanProtocolMode.Can20)
        {
            var baud = PcanUtils.MapClassicBaud(Options.BitTiming);
            var st = PcanIsoTp.Initialize(_handle, baud);
            if (st != PcanIsoTp.PCanTpStatus.Ok)
            {
                throw new CanBusCreationException($"PCAN Initialize failed: {st}");
            }
            CanKitLogger.LogInformation("PCAN: Initialize (classic) succeeded.");
        }
    }

    private readonly IBusRTOptionsConfigurator _busOptions;
    private readonly PcanChannel _handle;
    public IIsoTpRTConfigurator Options { get; }
    public BusNativeHandle NativeHandle { get; }
    public event EventHandler<IsoTpDatagram>? DatagramReceived;
    public Task<bool> SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default) => throw new NotImplementedException();

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}
