using CanKit.Protocol.IsoTp.Defines;

namespace CanKit.Protocol.IsoTp.Core;

public sealed class IsoTpChannel : IDisposable
{
    internal IsoTpChannelCore Core { get; }
    internal IsoTpScheduler Scheduler { get; }
    internal IsoTpOptions Options { get; }

    public IsoTpEndpoint Endpoint => Core.Endpoint;


    public event EventHandler<IsoTpDatagram>? DatagramReceived;

    internal IsoTpChannel(IsoTpChannelCore core, IsoTpScheduler sch, IsoTpOptions opt)
    { Core = core; Scheduler = sch; Options = opt; }


    public Task SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default)
    {
        Core.BeginSend(pdu.Span, Options.ClassicCanPadding, Options.UseCanFd, Options.N_Bs);
        return Task.CompletedTask;
    }


    public async Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default)
    {
        Core.BeginSend(request.Span, Options.ClassicCanPadding, Options.UseCanFd, Options.N_Bs);
        await Task.Yield();
        throw new NotImplementedException("Hook Rx completion here.");
    }

    public void Dispose() => Core.Dispose();
}
