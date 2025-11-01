using System.Diagnostics;

namespace CanKit.Protocol.IsoTp.Defines;

internal sealed class Deadline(TimeSpan timeOut, bool actived = false)
{
    private Stopwatch _stopwatch = new();

    private bool _actived = actived;

    private readonly TimeSpan _timeOut = timeOut;
    private bool Actived => _actived;
    public bool TimeOut => _stopwatch.Elapsed > _timeOut;

    public TimeSpan Remaining => (_timeOut - _stopwatch.Elapsed);

    public void Reset() => _stopwatch.Reset();

    public void Restart() => _stopwatch.Restart();

    public void SetActive(bool newActive)
    {
        if (newActive && !_stopwatch.IsRunning)
            _stopwatch.Start();
        else if (!newActive && _stopwatch.IsRunning)
            _stopwatch.Stop();
    }
}
