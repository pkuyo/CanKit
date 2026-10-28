using System;
using System.Buffers;
using System.Threading;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core.Definitions;

namespace CanKit.Tests.Utils;

/// <summary>Tracks buffer ownership, optionally pausing or observing a successful rental.</summary>
internal sealed class CountingBufferAllocator(Action? onRent = null) : IBufferAllocator
{
    private readonly ArrayPoolBufferAllocator _inner = new();
    private int _rented;
    private int _returned;
    private int _outstanding;

    public int Rented => Volatile.Read(ref _rented);
    public int Returned => Volatile.Read(ref _returned);
    public int Outstanding => Volatile.Read(ref _outstanding);
    public bool FrameNeedDispose => true;

    public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
    {
        var owner = new TrackedOwner(_inner.Rent(length, zeroFill), this);
        Interlocked.Increment(ref _rented);
        Interlocked.Increment(ref _outstanding);
        try
        {
            onRent?.Invoke();
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private sealed class TrackedOwner(IMemoryOwner<byte> inner, CountingBufferAllocator allocator) : IMemoryOwner<byte>
    {
        private int _disposed;
        public Memory<byte> Memory => inner.Memory;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            inner.Dispose();
            Interlocked.Increment(ref allocator._returned);
            Interlocked.Decrement(ref allocator._outstanding);
        }
    }
}
