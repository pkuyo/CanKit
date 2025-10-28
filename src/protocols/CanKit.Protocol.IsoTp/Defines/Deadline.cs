using System.Diagnostics;

namespace CanKit.Protocol.IsoTp.Defines;

internal sealed class Deadline
{
    private long _ticks;
    public bool Armed => _ticks != 0;
    public void Arm(TimeSpan ts) => _ticks = Stopwatch.GetTimestamp() + (long)(ts.TotalSeconds * Stopwatch.Frequency);
    public void Disarm() => _ticks = 0;
    public bool Expired() => Armed && Stopwatch.GetTimestamp() > _ticks;
}
