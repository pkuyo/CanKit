using System;
using System.Buffers;

namespace CanKit.Abstractions.API.Transport.Definitions;

public record IsoTpDatagram(
    IMemoryOwner<byte> Owner,
    IsoTpEndpoint Endpoint) : IDisposable
{
    public ReadOnlyMemory<byte> Memory => Owner.Memory;

    public void Dispose()
    {
        Owner.Dispose();
    }

}
