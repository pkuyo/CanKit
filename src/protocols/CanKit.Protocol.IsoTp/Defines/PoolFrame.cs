using System.Buffers;
using CanKit.Core.Definitions;

namespace CanKit.Protocol.IsoTp.Defines;

internal sealed class PoolFrame : IDisposable
{
    public PoolFrame(ICanFrame frame, byte[] data)
    {
        CanFrame = frame;
        Data = data;
    }


    public ICanFrame CanFrame { get; init; }
    public byte[] Data { get; init; }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(Data);
    }
}
