using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Transport.Definitions;

namespace CanKit.Abstractions.API.Transport;

public interface IIsoTpChannel : IDisposable
{
    IsoTpEndpoint Endpoint { get; }

    event EventHandler<IsoTpDatagram>? DatagramReceived;

    Task<bool> SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default);

    Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default);
}
