using System;
using System.Buffers;
using System.Threading;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;

namespace CanKit.Core.Definitions;

/// <summary>
/// Buffer allocator backed by <see cref="ArrayPool{T}"/>, returning pooled buffers
/// that must be disposed to return memory to the pool.
/// 基于 <see cref="ArrayPool{T}"/> 的缓冲区分配器；返回的缓冲区来自共享池，需在使用完毕后释放以归还内存。
/// </summary>
public sealed class ArrayPoolBufferAllocator : IBufferAllocator
{
    private readonly ArrayPool<byte> _pool;
    private readonly bool _clearOnReturn;

    public ArrayPoolBufferAllocator(ArrayPool<byte>? pool = null, bool clearOnReturn = false)
    {
        _pool = pool ?? ArrayPool<byte>.Shared;
        _clearOnReturn = clearOnReturn;
    }

    public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
    {
        if (length == 0)
        {
            return new Owner(_pool, Array.Empty<byte>(), false);
        }
        var buffer = _pool.Rent(length);
        if (zeroFill)
            Array.Clear(buffer, 0, length);
        return new Owner(_pool, buffer, _clearOnReturn);
    }

    public bool FrameNeedDispose => true;

    private sealed class Owner : IMemoryOwner<byte>
    {
        private ArrayPool<byte>? _pool;
        private byte[]? _buffer;
        private readonly bool _clearOnReturn;

        public Owner(ArrayPool<byte> pool, byte[] buffer, bool clearOnReturn)
        {
            _pool = pool;
            _buffer = buffer;
            _clearOnReturn = clearOnReturn;
        }

        public Memory<byte> Memory => _buffer!;

        public void Dispose()
        {
            var buf = Interlocked.Exchange(ref _buffer, null);
            var pool = Interlocked.Exchange(ref _pool, null);
            if (buf != null && buf.Length != 0 && pool != null)
                pool.Return(buf, _clearOnReturn);
        }
    }
}
