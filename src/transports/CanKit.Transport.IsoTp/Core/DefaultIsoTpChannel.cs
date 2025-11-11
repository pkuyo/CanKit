using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;

namespace CanKit.Transport.IsoTp.Core;

public sealed class DefaultIsoTpChannel : IIsoTpChannel
{
    internal IsoTpChannelCore Core { get; }
    internal IsoTpScheduler Scheduler { get; }
    public IsoTpOptions Options { get; }
    public BusNativeHandle NativeHandle { get; } = BusNativeHandle.Zero;

    public IsoTpEndpoint Endpoint => Core.Endpoint;


    public event EventHandler<IsoTpDatagram>? DatagramReceived;

    internal DefaultIsoTpChannel(IsoTpChannelCore core, IsoTpScheduler sch, IsoTpOptions opt)
    {
        Core = core;
        Scheduler = sch;
        Options = opt;
        Core.DatagramReceived += OnCoreDatagramReceived;
    }


    public Task<bool> SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default)
    {
        return Core.SendAsync(pdu.Span, Options.CanPadding, Options.Protocol == CanProtocolMode.CanFd, ct);
    }


    public async Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default)
    {
        await Core.SendAsync(request.Span, Options.CanPadding, Options.Protocol == CanProtocolMode.CanFd, ct);
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<IsoTpDatagram>> ReceiveAsync(int count, int timeOutMs = 0, CancellationToken ct = default) => throw new NotImplementedException();

    public IAsyncEnumerable<IsoTpDatagram> GetFramesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public void Dispose()
    {
        Core.DatagramReceived -= OnCoreDatagramReceived;
        Core.Dispose();
    }

    private void OnCoreDatagramReceived(object? sender, IsoTpDatagram datagram)
        => DatagramReceived?.Invoke(this, datagram);
}
