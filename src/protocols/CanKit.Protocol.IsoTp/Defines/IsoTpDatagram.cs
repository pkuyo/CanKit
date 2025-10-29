using System.Buffers;

namespace CanKit.Protocol.IsoTp.Defines;

public  class IsoTpDatagram : IDisposable
{
    public Memory<byte> Memory => _owner.Memory;
    public int Length { get; }
    public IsoTpEndpoint Endpoint { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CompletedAt { get; }
    public int FrameCount { get; }
    public bool IsFunctional { get; }
    public bool IsCanFd { get; }
    public bool Padded { get; }
    public Guid CorrelationId { get; }

    private readonly IMemoryOwner<byte> _owner;
    public void Dispose()
    {

    }
}
