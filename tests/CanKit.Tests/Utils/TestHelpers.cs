using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Tests.Utils;

internal static class TestHelpers
{
    public static ICanBus OpenClassic(string endpoint, bool enableFallbackAll = true, int bitrate = 500_000,
        int asyncBuf = 8192)
        => CanBus.Open(endpoint, cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(bitrate);
            TestCaseProvider.Provider.TestBusInitFunc?.Invoke(cfg);
            if (enableFallbackAll) cfg.SoftwareFeaturesFallBack(CanFeature.All);
            cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200);
        });

    public static ICanBus OpenFd(string endpoint, bool enableFallbackAll = true,
        int abit = 1_000_000, int dbit = 8_000_000, int asyncBuf = 8192)
    {

        return CanBus.Open(endpoint, cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.CanFd).Fd(abit, dbit);
            TestCaseProvider.Provider.TestBusInitFunc?.Invoke(cfg);
            if (enableFallbackAll) cfg.SoftwareFeaturesFallBack(CanFeature.All);
            cfg.EnableErrorInfo().SetAsyncBufferCapacity(asyncBuf).SetReceiveLoopStopDelayMs(200);
        });

    }

    public static ICanFrame[] CreateClassicSeq(uint baseId, bool extended, bool rtr, int len)
    {
        len = Math.Max(len, 0);
        if (rtr)
        {
            len = 0;
        }
        var ring = new ICanFrame[256];
        for (var seq = 0; seq < 256; seq++)
        {
            var payload = new byte[Math.Min(Math.Max(len, 0), 8)];
            if (payload.Length > 0) payload[0] = (byte)seq;
            for (var i = 1; i < payload.Length; i++) payload[i] = (byte)(i ^ seq);
            ring[seq] = new CanClassicFrame((int)(baseId | (uint)seq), payload, extended, rtr);
        }
        return ring;
    }

    public static ICanFrame[] CreateFdSeq(uint baseId, bool extended, bool brs, int len)
    {
        len = Math.Min(Math.Max(len, 0), 64);
        var ring = new ICanFrame[256];
        for (var seq = 0; seq < 256; seq++)
        {
            var payload = new byte[len];
            if (payload.Length > 0) payload[0] = (byte)seq;
            for (var i = 1; i < payload.Length; i++) payload[i] = (byte)(i ^ seq);
            ring[seq] = new CanFdFrame((int)(baseId | (uint)seq), payload, brs, false, extended);
        }
        return ring;
    }

    public sealed class SequenceVerifier
    {
        private readonly object _lock = new();
        private int _expected;
        public int Received { get; private set; }
        public int Lost { get; private set; }
        public int Duplicates { get; private set; }
        public int OutOfOrder { get; private set; }
        public int BadData { get; private set; }

        public void Feed(ICanFrame fr)
        {
            lock (_lock)
            {
                var seqFromId = fr.ID & 0xFF;
                var seqFromPayload = fr.Data.Length > 0 ? fr.Data.Span[0] : seqFromId;
                if (seqFromPayload != seqFromId)
                {
                    BadData++;
                }
                var delta = (seqFromId - _expected) & 0xFF;
                if (delta == 0)
                {
                    _expected = (_expected + 1) & 0xFF;
                }
                else
                {
                    if (delta == 0xFF)
                    {
                        Duplicates++;
                    }
                    else if (delta < 0x80)
                    {
                        Lost += delta;
                    }
                    else
                    {
                        OutOfOrder++;
                    }
                    _expected = (seqFromId + 1) & 0xFF;
                }
                Received++;
            }
        }
    }

    public static async Task<int> ReceiveUntilAsync(ICanBus bus, SequenceVerifier v, int expected, int timeoutMs, CancellationToken token)
    {
        var result = 0;
        var sw = new Stopwatch();
        while (!token.IsCancellationRequested && v.Received < expected && sw.ElapsedMilliseconds < timeoutMs)
        {

                var batch = await bus.ReceiveAsync(Math.Min(256, expected - v.Received), 20, token);
                foreach (var d in batch)
                {
                    result++;
                    v.Feed(d.CanFrame);
                }


        }
        return result;
    }

    public static async Task SendBurstAsync(ICanBus tx, IEnumerable<ICanFrame> frames, int gapMs)
    {
        var queue = new Queue<ICanFrame>();
        foreach(var fr in frames)
        {
            queue.Enqueue(fr);

            if (queue.Count >= 64)
            {
                while (queue.Count > 0)
                {
                    int send = await tx.TransmitAsync(queue, 20);

                    for (var j = 0; j < send && queue.Count > 0; j++)
                        queue.Dequeue();
                    if (queue.Count == 0)
                    {
                        if (gapMs > 0)
                            await Task.Delay(gapMs);
                        break;

                    }

                    await Task.Delay(1);
                }
            }
        }

        while (queue.Count > 0)
        {
            int send = await tx.TransmitAsync(queue, 20).ConfigureAwait(false);

            for (var j = 0; j < send && queue.Count > 0; j++)
                queue.Dequeue();
            if (queue.Count == 0)
                break;

            await Task.Delay(1);
        }
    }

    public static IEnumerable<ICanFrame> GenerateSeqFrames(ICanFrame[] ring, int count)
    {
        var seq = 0;
        for (var i = 0; i < count; i++)
        {
            yield return ring[seq & 0xFF];
            seq = (seq + 1) & 0xFF;
        }
    }
}

