using System;
using System.Collections.Generic;
using System.Diagnostics;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Core.Utils;

/// <summary>
/// Estimates classical CAN bus utilization from observed RX frames and accepted TX frames.
/// Only traffic visible to this bus instance is included.
/// </summary>
internal sealed class SoftwareBusUsageEstimator
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
    private readonly object _gate = new();
    private readonly Queue<FrameSample> _samples = new();
    private readonly Func<long> _timestamp;
    private long _bits;
    private readonly long _startedAt;

    internal SoftwareBusUsageEstimator() : this(MonotonicTimestamp) { }

    private static long MonotonicTimestamp() =>
        (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);

    internal SoftwareBusUsageEstimator(Func<long> timestamp)
    {
        _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
        _startedAt = timestamp();
    }

    public void Observe(in CanFrameView frame)
    {
        if (frame.IsErrorFrame || frame.FrameKind != CanFrameType.Can20) return;
        Add(FrameBitCount(frame));
    }

    public void Observe(in CanFrame frame)
    {
        if (frame.IsErrorFrame || frame.FrameKind != CanFrameType.Can20) return;
        Add(FrameBitCount(new CanFrameView(frame)));
    }

    public float GetUsage(uint bitrate)
    {
        if (bitrate == 0) throw new ArgumentOutOfRangeException(nameof(bitrate));

        lock (_gate)
        {
            var now = _timestamp();
            Prune(now);
            var elapsed = Math.Min(Window.Ticks, Math.Max(1, now - _startedAt));
            var usage = 100d * _bits * TimeSpan.TicksPerSecond / (bitrate * (double)elapsed);
            return (float)Math.Min(100d, usage);
        }
    }

    private void Add(int bits)
    {
        lock (_gate)
        {
            var now = _timestamp();
            Prune(now);
            _samples.Enqueue(new FrameSample(now, bits));
            _bits += bits;
        }
    }

    private void Prune(long now)
    {
        var cutoff = now - Window.Ticks;
        while (_samples.Count > 0 && _samples.Peek().Timestamp <= cutoff)
            _bits -= _samples.Dequeue().Bits;
    }

    internal static int FrameBitCount(in CanFrameView frame)
    {
        if (frame.FrameKind != CanFrameType.Can20)
            throw new NotSupportedException("Software bus usage currently supports classical CAN frames only.");

        var data = frame.Data.Span;
        var extended = frame.IsExtendedFrame;
        var crc = 0;
        var previous = -1;
        var run = 0;
        var stuffBits = 0;

        void ProcessBit(int bit, bool includeInCrc = true)
        {
            if (bit == previous)
            {
                run++;
                if (run == 5)
                {
                    stuffBits++;
                    run = 1;
                    previous = 1 - bit;
                }
            }
            else
            {
                previous = bit;
                run = 1;
            }

            if (includeInCrc)
            {
                var feedback = ((crc & 0x4000) != 0) ^ (bit != 0);
                crc = (crc << 1) & 0x7fff;
                if (feedback) crc ^= 0x4599;
            }
        }

        ProcessBit(0); // SOF
        if (extended)
        {
            for (var shift = 28; shift >= 18; shift--) ProcessBit((frame.ID >> shift) & 1);
            ProcessBit(1); // SRR
            ProcessBit(1); // IDE
            for (var shift = 17; shift >= 0; shift--) ProcessBit((frame.ID >> shift) & 1);
            ProcessBit(frame.IsRemoteFrame ? 1 : 0);
            ProcessBit(0); // r1
            ProcessBit(0); // r0
        }
        else
        {
            for (var shift = 10; shift >= 0; shift--) ProcessBit((frame.ID >> shift) & 1);
            ProcessBit(frame.IsRemoteFrame ? 1 : 0);
            ProcessBit(0); // IDE
            ProcessBit(0); // r0
        }

        var dlc = frame.Dlc;
        for (var shift = 3; shift >= 0; shift--) ProcessBit((dlc >> shift) & 1);
        if (!frame.IsRemoteFrame)
        {
            foreach (var value in data)
                for (var shift = 7; shift >= 0; shift--) ProcessBit((value >> shift) & 1);
        }

        for (var shift = 14; shift >= 0; shift--) ProcessBit((crc >> shift) & 1, false);
        return (extended ? 67 : 47) + (frame.IsRemoteFrame ? 0 : 8 * data.Length) + stuffBits;
    }

    private readonly struct FrameSample(long timestamp, int bits)
    {
        public long Timestamp { get; } = timestamp;
        public int Bits { get; } = bits;
    }
}
