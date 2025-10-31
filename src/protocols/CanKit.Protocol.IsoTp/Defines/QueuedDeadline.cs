using System.Diagnostics;
using CanKit.Core.Definitions;

namespace CanKit.Protocol.IsoTp.Defines;

public class QueuedDeadline
{
    private sealed class SlidingWindowStopwatch(TimeSpan timeOut)
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly SortedSet<TimeSpan> _starts = new();

        public TimeSpan AddStart()
        {
            _starts.Add(_sw.Elapsed);
            return _sw.Elapsed;
        }

        public bool Remove(TimeSpan timeSpan)
        {
            if (_starts.Count == 0) return false;
            _starts.Remove(timeSpan);
            return true;
        }

        public TimeSpan Elapsed =>
            _starts.Count == 0 ? TimeSpan.Zero : _sw.Elapsed - _starts.First();

        public TimeSpan Remaining => timeOut - Elapsed;

        public bool Timeout => Elapsed >= timeOut;
    }

    private readonly SlidingWindowStopwatch _nAs;

    private readonly Dictionary<ulong, TimeSpan> _hashTimeOutMap = new();

    public TimeSpan Remaining => _nAs.Remaining;

    public QueuedDeadline(TimeSpan nAs)
    {
        _nAs = new SlidingWindowStopwatch(nAs);
    }

    public void Enqueue(ICanFrame frame)
    {
        var hash = ComputeFrameHash(frame);
        _hashTimeOutMap.Add(hash, _nAs.AddStart());
    }

    public void Dequeue(ICanFrame frame)
    {
        var hash = ComputeFrameHash(frame);
        if (_hashTimeOutMap.TryGetValue(hash, out var timeSpan))
        {
            _nAs.Remove(timeSpan);
            _hashTimeOutMap.Remove(hash);
        }
    }

    private static ulong ComputeFrameHash(ICanFrame frame)
    {
        const ulong FNV_OFFSET = 1469598103934665603UL;
        const ulong FNV_PRIME = 1099511628211UL;
        ulong h = FNV_OFFSET;

        void MixByte(byte b) { h ^= b; h *= FNV_PRIME; }
        unchecked {
            for (int i = 0; i < 4; i++) MixByte((byte)((frame.ID >> (8*i)) & 0xFF));
        }

        if (frame is CanFdFrame fd)
        {
            MixByte((byte)((1 << 4) | ((fd.BitRateSwitch ? 1 : 0) << 3) |
                           ((fd.ErrorStateIndicator ? 1 : 0) << 2) |
                           ((frame.IsErrorFrame ? 1 : 0) << 1) |
                           (frame.IsExtendedFrame ? 1 : 0)));
        }
        else if (frame is CanClassicFrame classic)
        {
            MixByte((byte)(((classic.IsRemoteFrame ? 1 : 0) << 2) |
                           ((frame.IsErrorFrame ? 1 : 0) << 1) |
                           (frame.IsExtendedFrame ? 1 : 0)));
        }

        MixByte(frame.Dlc);

        var span = frame.Data.Span;
        for (int i = 0; i < span.Length; i++) MixByte(span[i]);

        return h;
    }
}
