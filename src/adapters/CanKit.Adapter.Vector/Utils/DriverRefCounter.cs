using System;
using System.Threading;

namespace CanKit.Adapter.Vector.Utils;

/// <summary>
/// Simple reference counter that invokes supplied callbacks when the first consumer acquires
/// and the last consumer releases.
/// </summary>
public sealed class DriverRefCounter
{
    private readonly Action _onFirst;
    private readonly Action _onLast;
    private int _refCount;

    public DriverRefCounter(Action onFirst, Action onLast)
    {
        _onFirst = onFirst ?? throw new ArgumentNullException(nameof(onFirst));
        _onLast = onLast ?? throw new ArgumentNullException(nameof(onLast));
    }

    public IDisposable Acquire()
    {
        var newCount = Interlocked.Increment(ref _refCount);
        if (newCount == 1)
        {
            try
            {
                _onFirst();
            }
            catch
            {
                if (Interlocked.Decrement(ref _refCount) == 0)
                    TryInvokeLastSafe();
                throw;
            }
        }
        return new Releaser(this);
    }

    private void Release()
    {
        var newCount = Interlocked.Decrement(ref _refCount);
        if (newCount < 0)
        {
            Interlocked.Exchange(ref _refCount, 0);
            throw new InvalidOperationException("Release called more times than Acquire.");
        }

        if (newCount == 0)
        {
            _onLast();
        }
    }

    private void TryInvokeLastSafe()
    {
        try { _onLast(); }
        catch { /* Swallow exceptions when rolling back a failed first acquire */ }
    }

    private sealed class Releaser : IDisposable
    {
        private DriverRefCounter? _owner;
        public Releaser(DriverRefCounter owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
