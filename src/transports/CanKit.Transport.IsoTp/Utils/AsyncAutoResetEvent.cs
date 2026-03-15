namespace CanKit.Protocol.IsoTp.Utils;

public sealed class AsyncAutoResetEvent
{
    private readonly SemaphoreSlim _sem = new(0, 1);

    public Task WaitAsync(CancellationToken ct = default)
        => _sem.WaitAsync(ct);

    public void Set()
    {
        if (_sem.CurrentCount == 0)
        {
            _sem.Release();
        }
    }

    public void Reset()
    {
        if (_sem.CurrentCount == 1)
        {
            _sem.Wait();
        }
    }
}
