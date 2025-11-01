namespace CanKit.Protocol.IsoTp.Utils;

public sealed class AsyncAutoResetEvent
{
    private readonly SemaphoreSlim _sem = new(0, 1);

    public Task WaitAsync(CancellationToken ct = default)
        => _sem.WaitAsync(ct);

    public void Set()
    {
        try { _sem.Release(); }
        catch (SemaphoreFullException) { /* already signaled; ignore */ }
    }
}
