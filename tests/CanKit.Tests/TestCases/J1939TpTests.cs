using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.J1939Tp;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Virtual-loopback integration tests for the J1939-21 §5.10 Transport Protocol implementation
/// in <c>CanKit.Pro.J1939Tp</c> (SRS FR-TP-030..035). Uses the same
/// <c>CanKit.Adapter.Virtual</c> pattern the other L2/L3 tests use, so a real bus is never
/// required and the tests are portable across every CI runner.
/// </summary>
public class J1939TpTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"j1939tp-{Guid.NewGuid():N}";

    // Both nodes share the same virtual bus (session-scoped) but appear as different channels of
    // the same VirtualBusHub, so a Transmit on one is seen by the other's FrameObserved. Every
    // test pins its own session to guarantee isolation.
    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static byte[] RandomPayload(int length, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[length];
        rng.NextBytes(buf);
        return buf;
    }

    // FR-TP-030 + FR-TP-032 + FR-TP-033: TP.BAM sender broadcasts a 100-byte PDU; the receiver
    // reassembles it identically from TP.CM(BAM) + TP.DT 1..15.
    [Fact]
    public async Task Bam_Roundtrip_ReceiverReassemblesIdenticalPayload()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // Shorten Th so the test runs in <1s while still exercising the timer.
        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5));

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x11, options: opts);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x22, options: opts);

        var payload = RandomPayload(100, seed: 42);
        var pgn = 0xFECAu; // arbitrary PDU2 broadcast PGN

        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendBamAsync(pgn, payload).WithTimeout(ShortTimeout);

        var datagram = await receiveTask;
        datagram.Kind.Should().Be(J1939TpKind.Bam);
        datagram.SourceAddress.Should().Be(0x11);
        datagram.DestinationAddress.Should().Be(0xFF);
        datagram.Pgn.Should().Be(pgn);
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-031 + FR-TP-032 + FR-TP-033: TP.CM sender emits RTS -> receiver replies CTS ->
    // sender streams TP.DT -> receiver reassembles and sends EndOfMsgAck -> sender's task
    // completes.
    [Fact]
    public async Task Cm_Roundtrip_ReceiverReassemblesAndAcks()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x01);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x02);

        var payload = RandomPayload(300, seed: 7);
        var pgn = 0xEF00u; // arbitrary PDU1 destination-addressed PGN

        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendCmAsync(pgn, destinationAddress: 0x02, payload).WithTimeout(ShortTimeout);

        var datagram = await receiveTask;
        datagram.Kind.Should().Be(J1939TpKind.Cm);
        datagram.SourceAddress.Should().Be(0x01);
        datagram.DestinationAddress.Should().Be(0x02);
        datagram.Pgn.Should().Be(pgn);
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-032 + FR-TP-033: exact 7*N boundary payload -- one full block of TP.DT frames --
    // reassembles correctly.
    [Fact]
    public async Task Cm_ExactBoundaryPayload_Reassembles()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x03);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x04);

        // 7 * 16 = 112 bytes -- one full CTS block at default MaxPacketsPerCts=16.
        var payload = RandomPayload(112, seed: 99);
        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);

        await sender.SendCmAsync(0xEF10, destinationAddress: 0x04, payload).WithTimeout(ShortTimeout);
        var datagram = await receiveTask;
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-034/035: two independent TP.CM sessions to different destinations run in parallel
    // over the same physical bus, plus a concurrent TP.BAM broadcast, all reassembled correctly
    // by the intended recipients.
    [Fact]
    public async Task Parallel_Bam_And_TwoCm_Sessions_Do_Not_Interfere()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busC = Open(session, 2);

        using var sender = J1939Tp.Open(busA, sourceAddress: 0x10);
        using var receiverB = J1939Tp.Open(busB, sourceAddress: 0xB0);
        using var receiverC = J1939Tp.Open(busC, sourceAddress: 0xC0);

        var payloadBam = RandomPayload(200, seed: 1);
        var payloadCmB = RandomPayload(400, seed: 2);
        var payloadCmC = RandomPayload(250, seed: 3);

        var pgnBam = 0xFEF0u;
        var pgnCmB = 0xEE10u;
        var pgnCmC = 0xEE20u;

        // Collect BAM on both receivers, CM only on its target.
        var collectB = CollectAsync(receiverB, count: 2, ShortTimeout);
        var collectC = CollectAsync(receiverC, count: 2, ShortTimeout);

        var t1 = sender.SendBamAsync(pgnBam, payloadBam);
        var t2 = sender.SendCmAsync(pgnCmB, destinationAddress: 0xB0, payloadCmB);
        var t3 = sender.SendCmAsync(pgnCmC, destinationAddress: 0xC0, payloadCmC);
        await Task.WhenAll(t1, t2, t3).WithTimeout(ShortTimeout);

        var listB = await collectB;
        var listC = await collectC;

        // ReceiverB must see the BAM (broadcast) and its own CM.
        listB.Should().HaveCount(2);
        var bamOnB = listB.Single(d => d.Kind == J1939TpKind.Bam);
        var cmOnB = listB.Single(d => d.Kind == J1939TpKind.Cm);
        bamOnB.Pgn.Should().Be(pgnBam);
        bamOnB.Payload.Should().Equal(payloadBam);
        cmOnB.Pgn.Should().Be(pgnCmB);
        cmOnB.Payload.Should().Equal(payloadCmB);

        // ReceiverC must see the BAM and its own CM (not the CM sent to B).
        listC.Should().HaveCount(2);
        var bamOnC = listC.Single(d => d.Kind == J1939TpKind.Bam);
        var cmOnC = listC.Single(d => d.Kind == J1939TpKind.Cm);
        bamOnC.Pgn.Should().Be(pgnBam);
        bamOnC.Payload.Should().Equal(payloadBam);
        cmOnC.Pgn.Should().Be(pgnCmC);
        cmOnC.Payload.Should().Equal(payloadCmC);
    }

    // FR-TP-032 negative path: with no peer to answer, the TP.CM sender's own T3 timer expires
    // and its SendCmAsync task faults with a J1939TpAbortException(Reason=Timeout).
    [Fact]
    public async Task Cm_NoPeer_TimesOutWithAbortException()
    {
        var session = NewSession();
        using var bus = Open(session, 0);

        // Only the sender is present; nobody will reply with CTS. Shorten T3 so the test runs
        // in ~200 ms instead of the standard-recommended 1250 ms.
        var opts = new J1939TpOptions().With(
            t2: TimeSpan.FromMilliseconds(150),
            t3: TimeSpan.FromMilliseconds(150));

        using var sender = J1939Tp.Open(bus, sourceAddress: 0x30, options: opts);

        var send = sender.SendCmAsync(0xEE30, destinationAddress: 0x99,
            RandomPayload(50, seed: 5));

        Func<Task> act = async () => await send.WithTimeout(ShortTimeout);
        var ex = (await act.Should().ThrowAsync<J1939TpAbortException>()).Which;
        ex.Reason.Should().Be(J1939TpAbortReason.Timeout);
        ex.Pgn.Should().Be(0xEE30u);
    }

    // FR-TP-030 lower-bound check: a 9-byte payload (the smallest legal J1939-TP payload;
    // anything ≤ 8 must use single-frame per §5.10.1) still round-trips correctly.
    [Fact]
    public async Task Bam_MinimumPayload_Roundtrip()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5));
        using var sender = J1939Tp.Open(busA, sourceAddress: 0x40, options: opts);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x41, options: opts);

        var payload = RandomPayload(9, seed: 11);
        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(ShortTimeout);
        await sender.SendBamAsync(0xFEEE, payload).WithTimeout(ShortTimeout);

        var datagram = await receiveTask;
        datagram.Payload.Should().Equal(payload);
    }

    // FR-TP-030 upper-bound check: the largest legal J1939-TP payload (1785 bytes = 255 * 7)
    // still round-trips via BAM. Exercises SN wrap all the way to 255 without off-by-one bugs.
    [Fact]
    public async Task Bam_MaximumPayload_Roundtrip()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // Th=1ms so 255 hold-offs together take ~0.25s rather than dominating test time; the
        // Virtual hub delivers synchronously so timing is not what we're checking here.
        var opts = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(1));
        using var sender = J1939Tp.Open(busA, sourceAddress: 0x50, options: opts);
        using var receiver = J1939Tp.Open(busB, sourceAddress: 0x51, options: opts);

        var payload = RandomPayload(1785, seed: 13);
        var receiveTask = receiver.ReceiveAsync().AsTaskWithTimeout(TimeSpan.FromSeconds(10));
        await sender.SendBamAsync(0xFEED, payload).WithTimeout(TimeSpan.FromSeconds(10));

        var datagram = await receiveTask;
        datagram.Payload.Should().Equal(payload);
    }

    private static async Task<List<J1939TpDatagram>> CollectAsync(IJ1939TpChannel channel, int count, TimeSpan timeout)
    {
        var list = new List<J1939TpDatagram>();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await foreach (var d in channel.ReceiveAllAsync(cts.Token))
            {
                list.Add(d);
                if (list.Count >= count) break;
            }
        }
        catch (OperationCanceledException)
        {
            // timeout -> return what we have
        }
        return list;
    }
}

internal static class J1939TpTestExtensions
{
    public static async Task<T> AsTaskWithTimeout<T>(this Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException($"Operation timed out after {timeout}.");
        return await task;
    }

    public static async Task WithTimeout(this Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task) throw new TimeoutException($"Operation timed out after {timeout}.");
        await task;
    }
}
