using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport.Definitions;

namespace CanKit.Abstractions.API.Transport;

public interface IIsoTpChannel : IDisposable
{
    IIsoTpRTConfigurator Options { get; }

    BusNativeHandle NativeHandle { get; }

    event EventHandler<IsoTpDatagram>? DatagramReceived;

    Task<bool> SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default);

    Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default);
}
