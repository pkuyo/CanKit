using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;

namespace CanKit.Protocol.IsoTp.Utils;

internal static class AsyncWaitUtils
{
    public static async Task<bool> WaitEventOrTimeoutAsync(
        AsyncAutoResetEvent evt, Deadline deadline, CancellationToken ct)
    {
        var delay = PreciseDelay.DelayAsync(deadline.Remaining, ct: ct);
        var wait = evt.WaitAsync(ct);
        var finished = await Task.WhenAny(wait, delay).ConfigureAwait(false);
        return finished == delay;
    }
}
