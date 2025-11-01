using System;
using System.Buffers;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Core.Definitions;

/// <summary>
/// Simple allocator that creates new byte arrays per request; disposing the owner is a no-op.
/// 简单分配器，每次请求新建字节数组；释放拥有者不执行任何实质操作。
/// </summary>
public sealed class DefaultBufferAllocator : IBufferAllocator
{
    public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
    {
        return new Owner(length == 0 ? Array.Empty<byte>() : new byte[length]);
    }

    private class Owner(Memory<byte> memory) : IMemoryOwner<byte>
    {
        public void Dispose()
        {
        }

        public Memory<byte> Memory { get; } = memory;
    }

    public bool FrameNeedDispose { get; } = false;
}
