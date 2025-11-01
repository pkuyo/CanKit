using System;
using System.Buffers;

namespace CanKit.Abstractions.API.Transport;

public class IsoTpDatagram(IMemoryOwner<byte> owner) : IDisposable
{
    public Memory<byte> Memory => _owner.Memory;
    public int Length { get; }
    public IsoTpEndpoint Endpoint { get; } = new();
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public int FrameCount { get; }
    public bool IsFunctional { get; }
    public bool IsCanFd { get; }
    public bool Padded { get; }
    public Guid CorrelationId { get; }

    private readonly IMemoryOwner<byte> _owner = owner;
    public void Dispose()
    {

    }
}
