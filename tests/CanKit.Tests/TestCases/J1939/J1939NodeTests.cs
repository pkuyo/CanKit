using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.Addressing;
using CanKit.Pro.J1939;
using CanKit.Pro.J1939Tp;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.J1939;

/// <summary>
/// Virtual-loopback integration tests for the SAE J1939 application-layer node
/// (<c>CanKit.Pro.J1939</c>), covering SRS FR-J1939-001..006 Must and FR-J1939-007 Should.
/// </summary>
public class J1939NodeTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"j1939-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    /// <summary>Constructs a NAME parameterized by a caller-controlled identity number so
    /// tests can force a deterministic winner in a claim conflict (numerically-lower NAME
    /// wins per SAE J1939-81 §4.4.3.2).</summary>
    private static J1939Name Name(uint identity, ushort manufacturerCode = 0x100) =>
        new J1939Name(
            identityNumber: identity,
            manufacturerCode: manufacturerCode,
            ecuInstance: 0,
            functionInstance: 0,
            function: 0x81,
            reserved: false,
            vehicleSystem: 0,
            vehicleSystemInstance: 0,
            industryGroup: 0,
            arbitraryAddressCapable: false);

    private static async Task<J1939Message> WaitForMessageAsync(
        IJ1939Node node,
        Func<J1939Message, bool> predicate,
        TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<J1939Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<J1939Message> handler = (_, msg) =>
        {
            if (predicate(msg)) tcs.TrySetResult(msg);
        };
        node.MessageReceived += handler;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            node.MessageReceived -= handler;
        }
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-001: PGN send/receive with 29-bit Priority/PF/PS/SA encode/decode.
    // ---------------------------------------------------------------------------------------

    // PDU2 (broadcast) PGN round-trip. Payload arrives on the receiver with the correct PGN,
    // priority and source address decoded back out of the 29-bit ID.
    [Fact]
    public async Task Pdu2_SingleFrameRoundtrip_DecodesPgnPriorityAndSa()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        using var receiver = J1939Node.Open(busB, new J1939NodeOptions(Name(2)));

        await sender.ClaimAddressAsync(0x11).WithTimeout(ShortTimeout);
        await receiver.ClaimAddressAsync(0x22).WithTimeout(ShortTimeout);

        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var message = new J1939Message(pgn: 0xFEF1u, payload: payload, priority: 5,
            destinationAddress: 0xFF); // PDU2, DA ignored

        var receiveTask = WaitForMessageAsync(receiver, m => m.Pgn == 0xFEF1u, ShortTimeout);
        await sender.SendAsync(message).WithTimeout(ShortTimeout);

        var received = await receiveTask;
        received.Pgn.Should().Be(0xFEF1u);
        received.Priority.Should().Be(5);
        received.SourceAddress.Should().Be(0x11);
        received.DestinationAddress.Should().Be(0xFF);
        received.Payload.ToArray().Should().Equal(payload);
        received.WasMultiFrame.Should().BeFalse();
    }

    // PDU1 (peer-to-peer) PGN round-trip. Destination address is preserved and the PGN group
    // extension is stripped correctly (PDU1 PS is a destination, not part of the PGN).
    [Fact]
    public async Task Pdu1_SingleFrameRoundtrip_DecodesDestinationAddress()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var sender = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        using var receiver = J1939Node.Open(busB, new J1939NodeOptions(Name(2)));

        await sender.ClaimAddressAsync(0x33).WithTimeout(ShortTimeout);
        await receiver.ClaimAddressAsync(0x44).WithTimeout(ShortTimeout);

        // 0xEF00 is a PDU1 PGN (PF=0xEF < 240). The wire ID encodes destination in PS.
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        var message = new J1939Message(pgn: 0xEF00u, payload: payload, priority: 6,
            destinationAddress: 0x44);

        var receiveTask = WaitForMessageAsync(receiver,
            m => m.Pgn == 0xEF00u && m.SourceAddress == 0x33, ShortTimeout);
        await sender.SendAsync(message).WithTimeout(ShortTimeout);

        var received = await receiveTask;
        received.Pgn.Should().Be(0xEF00u);
        received.SourceAddress.Should().Be(0x33);
        received.DestinationAddress.Should().Be(0x44);
        received.Payload.ToArray().Should().Equal(payload);
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-002: SPN scale/offset extraction.
    // ---------------------------------------------------------------------------------------

    // Well-known SPN 190 (Engine Speed, PGN 61444 / EEC1): 16-bit little-endian value at
    // byte offset 3, resolution 0.125 rpm/bit, offset 0. 8000 rpm ⇒ raw 64000 ⇒ 0x00 0xFA.
    [Fact]
    public void Spn_ExtractsScaleAndOffset()
    {
        var payload = new byte[8];
        // Byte 3 = 0x00, byte 4 = 0xFA (little-endian raw 64000).
        payload[3] = 0x00; payload[4] = 0xFA;
        double engineSpeed = J1939Spn.Extract(payload, byteOffset: 3, startBit: 0,
            bitLength: 16, resolution: 0.125, offset: 0.0);
        engineSpeed.Should().BeApproximately(8000.0, 0.001);
    }

    // Cross-byte-boundary 4-bit SPN with an offset (e.g. a temperature-style transform).
    [Fact]
    public void Spn_ExtractsCrossByteWithOffset()
    {
        var payload = new byte[] { 0b1111_0000, 0b0000_1010 };
        // Field spans byte 0 bits 4..7 and byte 1 bits 0..3 = 8 bits, little-endian.
        // = ( (0b0000_1010 & 0x0F) << 4 ) | ( (0b1111_0000 >> 4) & 0x0F ) = 0xAF = 175.
        double physical = J1939Spn.Extract(payload, byteOffset: 0, startBit: 4,
            bitLength: 8, resolution: 1.0, offset: -40.0);
        physical.Should().Be(175 - 40.0);
    }

    // Round-trip via WriteRaw so encoders and decoders are consistent.
    [Fact]
    public void Spn_WriteRaw_RoundTripsWithExtract()
    {
        var payload = new byte[8];
        J1939Spn.WriteRaw(payload, byteOffset: 2, startBit: 3, bitLength: 12, rawValue: 0xABC);
        J1939Spn.ExtractRaw(payload, byteOffset: 2, startBit: 3, bitLength: 12).Should().Be(0xABC);
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-003: Address claiming with NAME arbitration (winner keeps address, loser goes
    // to Cannot-Claim).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AddressClaim_TwoNodes_SameAddress_LowerNameWins()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        // Lower identity number ⇒ lower 64-bit NAME ⇒ higher claim priority (§4.4.3.2).
        var winnerName = Name(identity: 0x0000AA);
        var loserName = Name(identity: 0x0000BB);

        // Shrink the arbitration window so the test finishes quickly (default is 250 ms).
        var opts = (J1939NodeOptions optsFor) => optsFor;
        var optsWinner = new J1939NodeOptions(winnerName) { ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(200) };
        var optsLoser = new J1939NodeOptions(loserName) { ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(200) };

        using var winner = J1939Node.Open(busA, optsWinner);
        using var loser = J1939Node.Open(busB, optsLoser);

        // Both try 0x50 essentially concurrently; the actor loops each process the peer's
        // announcement and one of them yields.
        var winnerTask = winner.ClaimAddressAsync(0x50);
        var loserTask = loser.ClaimAddressAsync(0x50);

        // Winner (numerically-lower NAME) must succeed.
        await winnerTask.WithTimeout(ShortTimeout);
        winner.ClaimState.Should().Be(J1939ClaimState.Claimed);
        winner.Address.Should().Be((byte)0x50);

        // Loser must fault with J1939CannotClaimException per FR-J1939-004.
        Func<Task> act = () => loserTask.WithTimeout(ShortTimeout);
        var ex = (await act.Should().ThrowAsync<J1939CannotClaimException>()).Which;
        ex.PreferredAddress.Should().Be((byte)0x50);
        loser.ClaimState.Should().Be(J1939ClaimState.CannotClaim);
        loser.Address.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-004: Cannot-Claim broadcasts SA=0xFE.
    // ---------------------------------------------------------------------------------------

    // A node whose preferred address is contested by a peer with a lower NAME MUST broadcast
    // Cannot Claim Address (PGN 0xEE00, SA=0xFE) per SAE J1939-81 §4.4.3.4. We observe the
    // raw frame on the bus so the assertion is independent of the node's own transitions.
    [Fact]
    public async Task CannotClaim_BroadcastsWithNullSourceAddress()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busC = Open(session, 2); // spectator

        var cannotClaimSeen = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
        busC.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (J1939Pgn.IsAddressClaim(fields.Pgn) && fields.SourceAddress == J1939Pgn.NullAddress)
                cannotClaimSeen.TrySetResult((uint)e.CanFrame.ID);
        };

        var winnerOpts = new J1939NodeOptions(Name(0x000010)) { ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(200) };
        var loserOpts = new J1939NodeOptions(Name(0x000020)) { ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(200) };

        using var winner = J1939Node.Open(busA, winnerOpts);
        using var loser = J1939Node.Open(busB, loserOpts);

        var winnerTask = winner.ClaimAddressAsync(0x60);
        var loserTask = loser.ClaimAddressAsync(0x60);

        await winnerTask.WithTimeout(ShortTimeout);
        Func<Task> act = () => loserTask.WithTimeout(ShortTimeout);
        await act.Should().ThrowAsync<J1939CannotClaimException>();

        var canId = await cannotClaimSeen.Task.AsTaskWithTimeout(ShortTimeout);
        var decomposed = J1939Id.Decompose(canId);
        decomposed.SourceAddress.Should().Be(J1939Pgn.NullAddress);
        decomposed.PduSpecific.Should().Be(J1939Pgn.GlobalAddress);
        J1939Pgn.IsAddressClaim(decomposed.Pgn).Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-005: Request-PGN (PGN 0xEA00) send/receive.
    // ---------------------------------------------------------------------------------------

    // A requester sends Request-PGN(0xFEF1) to the global address; the responder application
    // observes the request on MessageReceived and answers with the requested PGN. The
    // requester's inbox then sees the answer.
    [Fact]
    public async Task RequestPgn_ResponderReceivesAndAppReplies()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        using var requester = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        using var responder = J1939Node.Open(busB, new J1939NodeOptions(Name(2)));

        await requester.ClaimAddressAsync(0x71).WithTimeout(ShortTimeout);
        await responder.ClaimAddressAsync(0x72).WithTimeout(ShortTimeout);

        const uint requestedPgn = 0xFEF1u;
        var answerPayload = new byte[] { 0x01, 0x02, 0x03 };

        // Responder listens for Request-PGN and answers with the requested PGN.
        responder.MessageReceived += async (_, msg) =>
        {
            if (msg.Pgn != J1939Pgn.Request) return;
            if (msg.Payload.Length < 3) return;
            uint askedFor = (uint)(msg.Payload.Span[0]
                | (msg.Payload.Span[1] << 8)
                | (msg.Payload.Span[2] << 16));
            if (askedFor != requestedPgn) return;
            try
            {
                await responder.SendAsync(new J1939Message(requestedPgn, answerPayload));
            }
            catch { /* observed via BackgroundExceptionOccurred */ }
        };

        var answerTask = WaitForMessageAsync(requester,
            m => m.Pgn == requestedPgn && m.SourceAddress == 0x72,
            ShortTimeout);

        await requester.RequestPgnAsync(requestedPgn).WithTimeout(ShortTimeout);
        var answer = await answerTask;
        answer.Payload.ToArray().Should().Equal(answerPayload);
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-006: > 8-byte payload routes through J1939-TP; <= 8-byte payload is direct.
    // ---------------------------------------------------------------------------------------

    // A ≤ 8-byte payload MUST NOT trigger any TP.CM/TP.DT frame on the bus; instead exactly
    // one direct 29-bit frame carries the PGN.
    [Fact]
    public async Task Send_SmallPayload_UsesSingleFramePath()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var spectator = Open(session, 2);

        using var sender = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        await sender.ClaimAddressAsync(0x81).WithTimeout(ShortTimeout);

        int tpFrames = 0;
        int singleFrames = 0;
        spectator.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != 0x81) return;
            if (J1939Pgn.IsTransportCm(fields.Pgn) || J1939Pgn.IsTransportDt(fields.Pgn))
                Interlocked.Increment(ref tpFrames);
            else if (fields.Pgn == 0xFEF2u)
                Interlocked.Increment(ref singleFrames);
        };

        await sender.SendAsync(new J1939Message(0xFEF2u, new byte[] { 1, 2, 3, 4, 5, 6 }))
            .WithTimeout(ShortTimeout);

        // Wait deterministically for exactly one single-frame observation instead of relying
        // on a fixed 50 ms sleep (Copilot 3600424648): the fixed delay was flaky on slow CI
        // runners, and the previous `> 0` assertion silently accepted duplicates.
        var deadline = DateTime.UtcNow + ShortTimeout;
        while (Volatile.Read(ref singleFrames) < 1 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Volatile.Read(ref tpFrames).Should().Be(0,
            "a ≤ 8-byte payload must not use J1939-TP");
        Volatile.Read(ref singleFrames).Should().Be(1,
            "exactly one direct 29-bit frame must carry the PGN");
    }

    [Fact]
    public async Task Send_InvalidPriority_ThrowsBeforeRouting()
    {
        var session = NewSession();
        using var busA = Open(session, 0);

        using var node = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        await node.ClaimAddressAsync(0x82).WithTimeout(ShortTimeout);

        var payloads = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
        };

        foreach (var payload in payloads)
        {
            Func<Task> send = () => node.SendAsync(new J1939Message(0xFEF2u, payload, priority: 8));
            var ex = (await send.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which;
            ex.ParamName.Should().Be("Priority");
        }
    }

    // A > 8-byte payload broadcast MUST use J1939-TP.BAM. We watch for TP.CM frames from the
    // sender on the bus and require the receiver's application PGN to arrive on
    // MessageReceived (proving the whole TP session ran through and reassembled).
    [Fact]
    public async Task Send_LargePayload_UsesJ1939TpBamPath()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var spectator = Open(session, 2);

        // Shorten Th so the multi-frame test runs in <1s while still exercising the timer.
        var senderOpts = new J1939NodeOptions(Name(1))
        {
            TransportOptions = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5)),
        };
        var receiverOpts = new J1939NodeOptions(Name(2))
        {
            TransportOptions = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5)),
        };

        using var sender = J1939Node.Open(busA, senderOpts);
        using var receiver = J1939Node.Open(busB, receiverOpts);

        await sender.ClaimAddressAsync(0x91).WithTimeout(ShortTimeout);
        await receiver.ClaimAddressAsync(0x92).WithTimeout(ShortTimeout);

        int tpCmSeen = 0;
        spectator.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != 0x91) return;
            if (J1939Pgn.IsTransportCm(fields.Pgn)) Interlocked.Increment(ref tpCmSeen);
        };

        var payload = new byte[20];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(0x40 + i);

        var recvTask = WaitForMessageAsync(receiver,
            m => m.Pgn == 0xFECAu && m.Payload.Length == 20, ShortTimeout);

        await sender.SendAsync(new J1939Message(0xFECAu, payload,
            destinationAddress: 0xFF)).WithTimeout(ShortTimeout);

        var received = await recvTask;
        received.Payload.ToArray().Should().Equal(payload);
        received.SourceAddress.Should().Be(0x91);
        received.WasMultiFrame.Should().BeTrue();
        Volatile.Read(ref tpCmSeen).Should().BeGreaterThan(0,
            ">8-byte payload must route through J1939-TP (a TP.CM announce must appear on the bus)");
    }

    // ---------------------------------------------------------------------------------------
    // Bugbot 3600377721 regression: after a successful address claim the node MUST accept
    // directed TP.CM traffic to the claimed SA. Before the fix J1939NodeImpl kept its internal
    // IJ1939TpChannel bound to the 0xFE placeholder SA even after ClaimState==Claimed, so
    // J1939TpChannel's `destination == SA || 0xFF` filter dropped every directed CM to the
    // claimed address.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task DirectedTpCm_ToClaimedAddress_IsReceivedAfterClaim()
    {
        var session = NewSession();
        using var busA = Open(session, 0); // peer: raw J1939-TP sender
        using var busB = Open(session, 1); // node under test

        // The receiver is a J1939 node — the whole point is to verify the *node* reassembles
        // and surfaces the directed multi-frame PDU on MessageReceived.
        using var receiver = J1939Node.Open(busB, new J1939NodeOptions(Name(2))
        {
            TransportOptions = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5)),
        });
        await receiver.ClaimAddressAsync(0xA0).WithTimeout(ShortTimeout);
        receiver.ClaimState.Should().Be(J1939ClaimState.Claimed);
        receiver.Address.Should().Be((byte)0xA0);

        // Peer sends a directed TP.CM (>8 bytes) targeting the claimed SA 0xA0. Uses a raw
        // J1939-TP channel from a different SA so the frames actually travel across the
        // virtual bus and hit the node's transport RX filter.
        using var peerTp = CanKit.Pro.J1939Tp.J1939Tp.Open(busA, sourceAddress: 0x55,
            new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(5)));

        var payload = new byte[24];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(0xB0 + i);

        var recvTask = WaitForMessageAsync(receiver,
            m => m.Pgn == 0xEF00u && m.Payload.Length == payload.Length && m.SourceAddress == 0x55,
            ShortTimeout);

        await peerTp.SendCmAsync(pgn: 0xEF00u, destinationAddress: 0xA0, payload)
            .WithTimeout(ShortTimeout);

        var received = await recvTask;
        received.Payload.ToArray().Should().Equal(payload);
        received.SourceAddress.Should().Be(0x55);
        received.DestinationAddress.Should().Be(0xA0);
        received.WasMultiFrame.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // Bugbot 3600440955 regression: cancelling ClaimAddressAsync during the arbitration
    // window MUST tear down the pending claim on the actor and prevent the arbitration timer
    // from later committing the address. Before the fix, the cancellation registration only
    // called TrySetCanceled on the returned task; OnClaimAnnounceElapsed still fired and
    // moved ClaimState to Claimed, silently contradicting the observed cancellation.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task ClaimAddressAsync_CancelDuringArbitration_TearsDownPendingClaim()
    {
        var session = NewSession();
        using var busA = Open(session, 0);

        // Long arbitration window so the test can cancel comfortably in the middle. 500 ms is
        // well above the actor scheduling jitter we need to observe.
        var opts = new J1939NodeOptions(Name(1))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(500),
        };
        using var node = J1939Node.Open(busA, opts);

        using var cts = new CancellationTokenSource();
        var claimTask = node.ClaimAddressAsync(0x33, cts.Token);

        // Give the actor a beat to enter Claiming so we know we cancel mid-arbitration and
        // not before BeginClaim has run.
        for (int i = 0; i < 20 && node.ClaimState != J1939ClaimState.Claiming; i++)
            await Task.Delay(10);
        node.ClaimState.Should().Be(J1939ClaimState.Claiming);

        cts.Cancel();

        // The task itself must complete as cancelled.
        Func<Task> awaitClaim = () => claimTask.WithTimeout(ShortTimeout);
        await awaitClaim.Should().ThrowAsync<TaskCanceledException>();

        // Wait past the original arbitration window so any surviving timer would have fired.
        await Task.Delay(700);

        // The node MUST NOT have silently committed to the cancelled address.
        node.ClaimState.Should().NotBe(J1939ClaimState.Claimed);
        node.Address.Should().BeNull();

        // A fresh claim must still work (i.e. teardown left the state machine consistent).
        await node.ClaimAddressAsync(0x44).WithTimeout(ShortTimeout);
        node.ClaimState.Should().Be(J1939ClaimState.Claimed);
        node.Address.Should().Be((byte)0x44);
    }

    // ---------------------------------------------------------------------------------------
    // Bugbot 3600614141 regression: when cts.Cancel() lands *at or after* the arbitration
    // deadline expires, OnClaimAnnounceElapsed can hit its "TCS already completed" early-
    // return branch (the token registration set TrySetCanceled before the cancel post
    // reached the actor) and the subsequent CancelPendingClaimOnLoop can then find
    // `_pendingClaim` already null. Before the fix, that pair left ClaimState stuck at
    // Claiming with no address and TP still bound to 0xFE — an unrecoverable-except-via-
    // BeginClaim state that both the caller (who saw TaskCanceled) and any observer
    // (who polls ClaimState) were told nothing about.
    //
    // The invariant this test enforces: regardless of which side of the deadline/cancel
    // race won on a given iteration, the settled ClaimState must NEVER remain at Claiming
    // (only NotClaimed or Claimed are legal terminal states after a canceled claim).
    // We use CancellationTokenSource.CancelAfter with the arbitration timeout so the two
    // timers (System.Threading.Timer for CT and DeadlineScheduler for the announce) race
    // on nearly the same wall-clock instant, spread over many iterations to sample both
    // sides of the race.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task ClaimAddressAsync_CancelAtArbitrationDeadline_NeverLeavesStateStuckInClaiming()
    {
        var session = NewSession();
        using var busA = Open(session, 0);

        // Tight arbitration window so CancelAfter and the announce deadline collide with
        // minimum jitter separation. Repeated iterations vary the exact interleave
        // through natural CI scheduling jitter across ThreadPool and the actor loop.
        var arbitrationTimeout = TimeSpan.FromMilliseconds(30);
        var opts = new J1939NodeOptions(Name(1))
        {
            ClaimAnnounceTimeout = arbitrationTimeout,
        };
        using var node = J1939Node.Open(busA, opts);

        for (int iter = 0; iter < 40; iter++)
        {
            byte preferred = (byte)(0x30 + (iter % 0x50));
            using var cts = new CancellationTokenSource();

            // Fire the cancel via System.Threading.Timer at (approximately) the same
            // instant the arbitration deadline fires on the actor loop. That maximises
            // the chance of catching the "OnClaimAnnounceElapsed observes a canceled
            // TCS" interleave the fix targets.
            cts.CancelAfter(arbitrationTimeout);

            var claimTask = node.ClaimAddressAsync(preferred, cts.Token);
            try
            {
                await claimTask.WithTimeout(ShortTimeout);
            }
            catch (OperationCanceledException) { /* cancel raced ahead of the deadline */ }
            // If claimTask completed successfully the deadline won and we ended up
            // Claimed at `preferred`; either outcome is acceptable — the invariant we
            // care about is that ClaimState never remains at Claiming after settle.

            // Give the actor loop time to fully unwind both callbacks (Fire and cancel
            // post). The deadline is short, but ThreadPool + actor scheduling means the
            // teardown can trail the awaited task by a few tens of ms.
            await Task.Delay(50);

            node.ClaimState.Should().NotBe(J1939ClaimState.Claiming,
                $"iteration {iter}: cancelling at the arbitration deadline must never leave the node stuck in Claiming");
            if (node.ClaimState == J1939ClaimState.NotClaimed)
                node.Address.Should().BeNull(
                    $"iteration {iter}: NotClaimed after cancel must have a null address");
        }

        // After the stress loop, the node's state machine must still be responsive: a
        // fresh uncancelled claim on a new SA must go through cleanly no matter which
        // race outcome dominated the loop above.
        await node.ClaimAddressAsync(0x50).WithTimeout(ShortTimeout);
        node.ClaimState.Should().Be(J1939ClaimState.Claimed);
        node.Address.Should().Be((byte)0x50);
    }

    // ---------------------------------------------------------------------------------------
    // Bugbot 3600377725 regression: starting a fresh ClaimAddressAsync on an already-claimed
    // node MUST invalidate the old SA immediately. SendAsync must reject application traffic
    // (throw J1939NoAddressException) until the new claim reaches Claimed again, otherwise the
    // node keeps transmitting with the old SA while its address-claim frame advertises a
    // different preferred one on the wire.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task ReClaim_RejectsSendUntilNewClaimSucceeds()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1); // spectator: watches which SAs appear on the wire

        // Give the arbitration window enough room that we can observe the mid-claim gap even on
        // a fast Virtual bus. 500 ms is well above CI jitter but short enough to keep the test
        // fast.
        var opts = new J1939NodeOptions(Name(1))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(500),
        };
        using var node = J1939Node.Open(busA, opts);

        // Initial claim -> we hold 0x11.
        await node.ClaimAddressAsync(0x11).WithTimeout(ShortTimeout);
        node.ClaimState.Should().Be(J1939ClaimState.Claimed);

        // A send with the initial claim succeeds; SA on the wire must be 0x11.
        byte? observedSa = null;
        busB.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.Pgn == 0xFEF3u) observedSa = fields.SourceAddress;
        };
        await node.SendAsync(new J1939Message(0xFEF3u, new byte[] { 1, 2, 3 })).WithTimeout(ShortTimeout);
        await Task.Delay(50);
        observedSa.Should().Be((byte)0x11);

        // Start a re-claim to a different preferred SA — do NOT await yet so we can inspect
        // the mid-claim behavior. The state must transition out of Claimed immediately.
        var reclaimTask = node.ClaimAddressAsync(0x22);

        // Give the actor a beat to process BeginClaim.
        for (int i = 0; i < 20 && node.ClaimState == J1939ClaimState.Claimed; i++)
            await Task.Delay(10);
        node.ClaimState.Should().NotBe(J1939ClaimState.Claimed,
            "starting a new claim must clear the previous Claimed state so old-SA traffic is gated off");
        node.Address.Should().BeNull(
            "the previous claimed address must be invalidated before the new preferred SA is announced");

        // SendAsync MUST reject application traffic while the claim is in-flight. Before the
        // fix, SendCoreAsync only checked _addressStore >= 0, so this send would silently go
        // out with the *previous* SA (0x11) while claim frames advertised 0x22.
        Func<Task> sendMidClaim = () => node.SendAsync(new J1939Message(0xFEF4u, new byte[] { 4, 5, 6 }));
        await sendMidClaim.Should().ThrowAsync<J1939NoAddressException>();

        // Once the new claim completes, application traffic MUST resume on the new SA.
        await reclaimTask.WithTimeout(ShortTimeout);
        node.ClaimState.Should().Be(J1939ClaimState.Claimed);
        node.Address.Should().Be((byte)0x22);

        byte? postSa = null;
        busB.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.Pgn == 0xFEF5u) postSa = fields.SourceAddress;
        };
        await node.SendAsync(new J1939Message(0xFEF5u, new byte[] { 7, 8, 9 }))
            .WithTimeout(ShortTimeout);
        await Task.Delay(50);
        postSa.Should().Be((byte)0x22);
    }

    // ---------------------------------------------------------------------------------------
    // Bugbot 3600591973 behavior lock: RebindTransportOnLoop MUST dispose the previous
    // IJ1939TpChannel synchronously on the actor loop BEFORE opening a new channel. Before
    // the fix, the old channel's Dispose was fire-and-forget while a fresh channel was
    // simultaneously opened, so both channels could remain briefly subscribed to the bus
    // and both accepted broadcast TP.BAM (DA = 0xFF). Reassembly and MessageReceived then
    // fired once per surviving channel, delivering the same BAM twice.
    //
    // The pre-fix window between Open and Task.Run(Dispose) is sub-millisecond on the
    // virtual bus, so this test cannot deterministically reproduce the race on every run;
    // it pins the correctness invariant (received <= sent) across many claim/re-claim
    // cycles under continuous broadcast BAM traffic. The synchronous-dispose fix makes the
    // invariant hold by construction.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task RebindTransport_DoesNotDeliverBamMoreThanOncePerRebind()
    {
        var session = NewSession();
        using var busPeer = Open(session, 0);
        using var busNode = Open(session, 1);

        // Short arbitration window so many rebinds happen while peer traffic is in flight;
        // small Th so a single BAM takes a couple of ms end-to-end.
        var opts = new J1939NodeOptions(Name(1))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(40),
            TransportOptions = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(2)),
        };
        using var node = J1939Node.Open(busNode, opts);

        int received = 0;
        node.MessageReceived += (_, m) =>
        {
            if (m.Pgn == 0xFED1u) Interlocked.Increment(ref received);
        };

        using var peerTp = CanKit.Pro.J1939Tp.J1939Tp.Open(busPeer, sourceAddress: 0x77,
            new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(2)));
        var payload = new byte[12];
        for (int b = 0; b < payload.Length; b++) payload[b] = (byte)(0xE0 + b);

        int sent = 0;
        using var peerCts = new CancellationTokenSource();
        var peerTask = Task.Run(async () =>
        {
            try
            {
                while (!peerCts.IsCancellationRequested)
                {
                    await peerTp.SendBamAsync(pgn: 0xFED1u, payload, peerCts.Token)
                        .ConfigureAwait(false);
                    Interlocked.Increment(ref sent);
                }
            }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch { /* channel disposed during shutdown */ }
        });

        // Cycle re-claims to a new SA every iteration. Each ClaimAddressAsync triggers two
        // RebindTransportOnLoop calls (unbind to 0xFE, then rebind to the new SA) — that is
        // where the old/new channel overlap window lived pre-fix.
        for (int i = 0; i < 8; i++)
        {
            byte sa = (byte)(0x30 + i);
            await node.ClaimAddressAsync(sa).WithTimeout(ShortTimeout);
            node.ClaimState.Should().Be(J1939ClaimState.Claimed);
            await Task.Delay(30);
        }

        peerCts.Cancel();
        try { await peerTask.WithTimeout(ShortTimeout); } catch { /* peer cancel/dispose */ }

        // Let any in-flight reassembly surface before the final count check.
        await Task.Delay(150);

        int finalSent = Volatile.Read(ref sent);
        int finalReceived = Volatile.Read(ref received);

        // The received count must never exceed sent: any excess means a rebind delivered
        // the same broadcast BAM through two overlapping node-side transports. (Received <
        // sent is expected — BAMs whose DT frames land during the ~ms rebind window get
        // aborted / dropped by the disposed channel and never reassembled by the new one.
        // The bug we are guarding against is duplicate delivery, not loss.)
        finalReceived.Should().BeLessOrEqualTo(finalSent,
            "no broadcast TP.BAM may be surfaced twice — before the fix, the fire-and-" +
            "forget Dispose of the previous channel overlapped a freshly-opened channel " +
            "and both subscriptions delivered the same reassembled datagram");
        // Sanity: this test is only meaningful if the peer actually managed to run many
        // BAMs across the rebind cycles.
        finalSent.Should().BeGreaterThan(5,
            "the peer must generate enough BAM traffic to exercise the rebind window");
    }

    // ---------------------------------------------------------------------------------------
    // Bugbot 3600591980 regression: SendCoreAsync checks ClaimState/address once before
    // awaiting the wire I/O. A concurrent ClaimAddressAsync running on the actor loop can
    // clear or move the SA mid-flight, and before the fix the send task completed
    // successfully — the frame went out on the previous SA while the wire simultaneously
    // advertised a different preferred address (or the multi-frame session was interrupted
    // by RebindTransportOnLoop with an internal ObjectDisposedException surfacing). The
    // send MUST fail with J1939NoAddressException so the failure mode matches the pre-send
    // gate on ClaimState==Claimed.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task Send_InFlightAcrossReclaim_FailsWithNoAddressException()
    {
        var session = NewSession();
        using var busA = Open(session, 0);

        // Longer Th so a multi-frame TP.BAM stays on the wire long enough for us to start a
        // re-claim while the send is still awaiting the last TP.DT.
        var opts = new J1939NodeOptions(Name(1))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(200),
            TransportOptions = new J1939TpOptions().With(th: TimeSpan.FromMilliseconds(60)),
        };
        using var node = J1939Node.Open(busA, opts);
        await node.ClaimAddressAsync(0x11).WithTimeout(ShortTimeout);
        node.Address.Should().Be((byte)0x11);

        // Multi-frame BAM: 60 bytes → 9 TP.DT frames at Th ≈ 60 ms each keeps the send task
        // awaiting for several hundred ms, giving us room to trigger a re-claim.
        var payload = new byte[60];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)i;
        var sendTask = node.SendAsync(new J1939Message(0xFED2u, payload, destinationAddress: 0xFF));

        // Wait for the actor to actually start the TP session before racing the re-claim
        // in; otherwise BeginClaim could run before SendCoreAsync captured the SA.
        for (int i = 0; i < 20 && !sendTask.IsCompleted && node.ClaimState == J1939ClaimState.Claimed; i++)
            await Task.Delay(10);

        // Kick off a reclaim to a different preferred address. BeginClaim clears the
        // captured address, and (with the Bugbot 3600591973 fix) synchronously disposes the
        // shared TP channel — either way SendCoreAsync must not report success.
        var reclaimTask = node.ClaimAddressAsync(0x22);

        Func<Task> awaitSend = () => sendTask.WithTimeout(ShortTimeout);
        await awaitSend.Should().ThrowAsync<J1939NoAddressException>(
            "an in-flight send whose captured SA was invalidated by a concurrent " +
            "ClaimAddressAsync must surface the same J1939NoAddressException as the pre-send gate");

        // The reclaim itself must still complete cleanly on the new SA — the send failure
        // does not tear down the claim state machine.
        await reclaimTask.WithTimeout(ShortTimeout);
        node.ClaimState.Should().Be(J1939ClaimState.Claimed);
        node.Address.Should().Be((byte)0x22);
    }

    // ---------------------------------------------------------------------------------------
    // FR-J1939-007: periodic single-frame PGN send. Every periodic PGN flows through the
    // node's SendAsync / actor loop (L2 scheduling) — the previous dual `IPeriodicTx` path
    // was collapsed to a single implementation (PR #33) so error handling and claim-gate
    // semantics are uniform across payload sizes. The test collects a run of frames on a
    // spectator bus and asserts the mean inter-arrival matches the caller's configured
    // period.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task StartPeriodicSend_SingleFrame_FiresAtConfiguredPeriod()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1); // spectator: samples arrival times

        using var sender = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        await sender.ClaimAddressAsync(0xC1).WithTimeout(ShortTimeout);

        // The stamp collection is protected by its own lock; the FrameObserved handler runs
        // on the bus's dispatch thread and multiple readers might in principle observe the
        // frame concurrently on some adapters.
        var stamps = new List<DateTime>();
        var stampsLock = new object();
        const uint targetPgn = 0xFEE5u; // PDU2, PS=0xE5 (arbitrary), well-known-ish
        busB.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.SourceAddress != 0xC1) return;
            if (fields.Pgn != targetPgn) return;
            lock (stampsLock) stamps.Add(DateTime.UtcNow);
        };

        // 80 ms period is comfortably above the ~1 ms virtual-loopback latency but short
        // enough to gather ≥8 samples in a couple of seconds without making the test flaky.
        var period = TimeSpan.FromMilliseconds(80);
        var payload = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        var message = new J1939Message(targetPgn, payload, priority: 6, destinationAddress: 0xFF);

        using (var handle = sender.StartPeriodicSend(message, period))
        {
            handle.Should().NotBeNull();

            // Collect until we have enough samples for a stable mean, or bail out with a
            // clear failure message if the schedule never fires.
            var deadline = DateTime.UtcNow + ShortTimeout;
            while (true)
            {
                int count;
                lock (stampsLock) count = stamps.Count;
                if (count >= 8) break;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"Expected at least 8 periodic emissions within {ShortTimeout.TotalSeconds}s; observed {count}.");
                await Task.Delay(20);
            }
        }

        // Post-Dispose: no additional frames should arrive after a settle window.
        int countAtDispose;
        lock (stampsLock) countAtDispose = stamps.Count;
        await Task.Delay(period + period); // wait 2 periods
        int countAfterSettle;
        lock (stampsLock) countAfterSettle = stamps.Count;

        countAfterSettle.Should().BeLessOrEqualTo(countAtDispose + 1,
            "disposing the handle must stop the periodic loop so at most an already-in-flight " +
            "SendAsync may still land after Dispose returns");

        // Inter-arrival timing. Compute the mean over the collected samples and assert it
        // matches the requested period to within a generous tolerance to survive CI jitter
        // (Virtual bus is fast but scheduling on shared runners can slip by tens of ms per
        // sample).
        List<DateTime> snapshot;
        lock (stampsLock) snapshot = new List<DateTime>(stamps);
        snapshot.Count.Should().BeGreaterOrEqualTo(8);

        var deltas = new List<double>(snapshot.Count - 1);
        for (int i = 1; i < snapshot.Count; i++)
            deltas.Add((snapshot[i] - snapshot[i - 1]).TotalMilliseconds);

        double mean = 0;
        foreach (var d in deltas) mean += d;
        mean /= deltas.Count;

        double targetMs = period.TotalMilliseconds;
        // Lower bound: the loop awaits Task.Delay(period) after each SendAsync, so mean
        // inter-arrival cannot be materially below the requested period. Upper bound:
        // tolerate CI jitter up to ~1.6x (send latency + Task.Delay drift).
        mean.Should().BeInRange(targetMs * 0.7, targetMs * 1.6,
            $"mean inter-arrival ({mean:F1} ms) should approximate the configured period ({targetMs:F0} ms)");
    }

    // The single-frame periodic path MUST refuse to start before ClaimAddressAsync completes,
    // mirroring SendAsync's pre-flight gate (Bugbot 3600377725) so no periodic traffic leaks
    // out with an invalid SA.
    [Fact]
    public async Task StartPeriodicSend_SingleFrame_BeforeClaim_ThrowsNoAddress()
    {
        var session = NewSession();
        using var busA = Open(session, 0);

        using var node = J1939Node.Open(busA, new J1939NodeOptions(Name(1)));
        node.ClaimState.Should().NotBe(J1939ClaimState.Claimed);

        var message = new J1939Message(0xFEE6u, new byte[] { 1, 2, 3 }, priority: 6,
            destinationAddress: 0xFF);

        Action act = () => node.StartPeriodicSend(message, TimeSpan.FromMilliseconds(50));
        act.Should().Throw<J1939NoAddressException>();

        // A subsequent successful claim + StartPeriodicSend must work.
        await node.ClaimAddressAsync(0xC2).WithTimeout(ShortTimeout);
        using var handle = node.StartPeriodicSend(message, TimeSpan.FromMilliseconds(80));
        handle.Should().NotBeNull();
    }

    // Bugbot 3603876664 regression: once the owning node loses its claim (a higher-priority
    // peer unseats it and it transitions to CannotClaim), the periodic schedule MUST stop
    // putting stale-SA frames on the wire. With the unified SendAsync path (PR #33),
    // SendAsync's pre-flight claim gate throws J1939NoAddressException on every subsequent
    // tick, so no CAN frame is emitted while the node is un-claimed. Assert the wire goes
    // quiet after unseating.
    [Fact]
    public async Task StartPeriodicSend_SingleFrame_StopsAfterAddressLoss()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busC = Open(session, 2); // spectator: counts periodic emissions

        // Owner has a HIGHER numeric NAME → lower priority → will be unseated when the
        // peer with a lower NAME claims the same SA per SAE J1939-81 §4.4.3.2.
        var ownerOpts = new J1939NodeOptions(Name(0x000200))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(80),
        };
        var peerOpts = new J1939NodeOptions(Name(0x000010))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(80),
        };

        using var owner = J1939Node.Open(busA, ownerOpts);
        using var peer = J1939Node.Open(busB, peerOpts);

        const byte contendedSa = 0x50;
        await owner.ClaimAddressAsync(contendedSa).WithTimeout(ShortTimeout);
        owner.ClaimState.Should().Be(J1939ClaimState.Claimed);
        owner.Address.Should().Be(contendedSa);

        // Watch for the periodic PGN on the spectator bus so the schedule's "still emitting"
        // assertion is independent of the owner node's internal state and matches what
        // downstream ECUs actually observe.
        const uint targetPgn = 0xFEE7u;
        var stamps = new List<DateTime>();
        var stampsLock = new object();
        busC.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.Pgn != targetPgn) return;
            if (fields.SourceAddress != contendedSa) return;
            lock (stampsLock) stamps.Add(DateTime.UtcNow);
        };

        var period = TimeSpan.FromMilliseconds(40);
        var message = new J1939Message(targetPgn, new byte[] { 0xA1, 0xA2 }, priority: 6,
            destinationAddress: 0xFF);
        using var handle = owner.StartPeriodicSend(message, period);

        // Wait until the schedule has actually put a few frames on the wire so the
        // "stop" assertion below is meaningful (the schedule really was running).
        var readyDeadline = DateTime.UtcNow + ShortTimeout;
        while (true)
        {
            int c;
            lock (stampsLock) c = stamps.Count;
            if (c >= 3) break;
            if (DateTime.UtcNow >= readyDeadline)
                throw new TimeoutException("Expected ≥3 periodic frames from owner before contest.");
            await Task.Delay(10);
        }

        // Peer with lower NAME claims the same SA. HandleIncomingAddressClaim's
        // "already claimed at SA + peer wins" branch flips the owner to CannotClaim and
        // clears its address, so subsequent SendAsync calls from the periodic loop fail
        // fast at the claim gate — no more frames go out under the previous SA.
        await peer.ClaimAddressAsync(contendedSa).WithTimeout(ShortTimeout);
        peer.Address.Should().Be(contendedSa);

        // Wait for the owner's state machine to observe the contest.
        var lossDeadline = DateTime.UtcNow + ShortTimeout;
        while (owner.ClaimState == J1939ClaimState.Claimed && DateTime.UtcNow < lossDeadline)
            await Task.Delay(10);
        owner.ClaimState.Should().NotBe(J1939ClaimState.Claimed);
        owner.Address.Should().BeNull();

        // Give the schedule ~2 periods to observe the state transition and let the
        // in-flight SendAsync (if any) drain. Peer traffic on `contendedSa` is filtered by
        // NAME (owner's Name(0x200) ≠ peer's Name(0x010)), so any frames on `contendedSa`
        // that arrive here originate from the owner's periodic loop *not yet stopping* —
        // that is exactly the bug we are guarding against.
        int countAfterLoss;
        lock (stampsLock) countAfterLoss = stamps.Count;
        await Task.Delay(period + period + TimeSpan.FromMilliseconds(50));
        int countAfterQuiet;
        lock (stampsLock) countAfterQuiet = stamps.Count;

        // We tolerate at most one already-in-flight emission slipping past the state
        // transition. Anything more means the loop kept sending under a stale SA.
        (countAfterQuiet - countAfterLoss).Should().BeLessOrEqualTo(1,
            "the periodic loop must stop putting frames on the wire within ~2 periods " +
            "after the owner loses its claim; otherwise stale-SA frames would keep going " +
            "out under the previous address (Bugbot 3603876664)");
    }

    // Bugbot 3604386825 regression: send failures inside the periodic loop MUST reach the
    // application via BackgroundExceptionOccurred. Now that every periodic PGN uses the
    // SendAsync-based PeriodicSchedule (PR #33), that means: after the owner loses its
    // claim, SendAsync's pre-flight gate throws J1939NoAddressException on the next tick
    // and the schedule surfaces the exception. The earlier dual-path implementation had a
    // silent-error hole when the L1 fallback swallowed Transmit exceptions; this test
    // guards against that regression coming back.
    [Fact]
    public async Task StartPeriodicSend_SingleFrame_SurfacesSendErrors()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);

        var ownerOpts = new J1939NodeOptions(Name(0x000200))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(80),
        };
        var peerOpts = new J1939NodeOptions(Name(0x000010))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(80),
        };

        using var owner = J1939Node.Open(busA, ownerOpts);
        using var peer = J1939Node.Open(busB, peerOpts);

        var backgroundExceptions = new List<Exception>();
        var exLock = new object();
        owner.BackgroundExceptionOccurred += (_, ex) =>
        {
            lock (exLock) backgroundExceptions.Add(ex);
        };

        const byte contendedSa = 0x53;
        await owner.ClaimAddressAsync(contendedSa).WithTimeout(ShortTimeout);
        owner.Address.Should().Be(contendedSa);

        var period = TimeSpan.FromMilliseconds(40);
        var message = new J1939Message(0xFEE9u, new byte[] { 0xC1, 0xC2 }, priority: 6,
            destinationAddress: 0xFF);
        using var handle = owner.StartPeriodicSend(message, period);

        // Peer unseats the owner → SendAsync's claim gate starts throwing
        // J1939NoAddressException on every scheduled emission. PeriodicSchedule.LoopAsync
        // catches non-cancellation exceptions and forwards them to
        // BackgroundExceptionOccurred so applications observe the failure.
        await peer.ClaimAddressAsync(contendedSa).WithTimeout(ShortTimeout);
        var lossDeadline = DateTime.UtcNow + ShortTimeout;
        while (owner.ClaimState == J1939ClaimState.Claimed && DateTime.UtcNow < lossDeadline)
            await Task.Delay(10);
        owner.ClaimState.Should().NotBe(J1939ClaimState.Claimed);

        // Give the schedule a few periods to attempt emissions under the lost claim.
        var seenDeadline = DateTime.UtcNow + ShortTimeout;
        while (true)
        {
            bool seen;
            lock (exLock) seen = backgroundExceptions.Exists(e => e is J1939NoAddressException);
            if (seen) break;
            if (DateTime.UtcNow >= seenDeadline)
                throw new TimeoutException(
                    "Expected the schedule to surface J1939NoAddressException via " +
                    "BackgroundExceptionOccurred after address loss — periodic send " +
                    "errors must not be silently swallowed (Bugbot 3604386825).");
            await Task.Delay(10);
        }
    }

    // Optional coverage for the reclaim-with-new-SA path: after the schedule stops on
    // address loss, a subsequent successful claim (potentially on a different SA) MUST
    // re-arm the periodic emission and the wire ID MUST carry the new SA.
    [Fact]
    public async Task StartPeriodicSend_SingleFrame_ReclaimResumesUnderNewSa()
    {
        var session = NewSession();
        using var busA = Open(session, 0);
        using var busB = Open(session, 1);
        using var busC = Open(session, 2); // spectator

        var ownerOpts = new J1939NodeOptions(Name(0x000200))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(80),
        };
        var peerOpts = new J1939NodeOptions(Name(0x000010))
        {
            ClaimAnnounceTimeout = TimeSpan.FromMilliseconds(80),
        };

        using var owner = J1939Node.Open(busA, ownerOpts);
        using var peer = J1939Node.Open(busB, peerOpts);

        const byte firstSa = 0x51;
        const byte secondSa = 0x52;
        await owner.ClaimAddressAsync(firstSa).WithTimeout(ShortTimeout);

        const uint targetPgn = 0xFEE8u;
        var newSaStamps = 0;
        busC.FrameObserved += (_, e) =>
        {
            if (!e.CanFrame.IsExtendedFrame) return;
            var fields = J1939Id.Decompose((uint)e.CanFrame.ID);
            if (fields.Pgn != targetPgn) return;
            if (fields.SourceAddress == secondSa) Interlocked.Increment(ref newSaStamps);
        };

        var period = TimeSpan.FromMilliseconds(40);
        var message = new J1939Message(targetPgn, new byte[] { 0xB1 }, priority: 6,
            destinationAddress: 0xFF);
        using var handle = owner.StartPeriodicSend(message, period);

        // Peer contests the first SA; owner is unseated → schedule tears down.
        await peer.ClaimAddressAsync(firstSa).WithTimeout(ShortTimeout);
        var lossDeadline = DateTime.UtcNow + ShortTimeout;
        while (owner.ClaimState == J1939ClaimState.Claimed && DateTime.UtcNow < lossDeadline)
            await Task.Delay(10);
        owner.ClaimState.Should().NotBe(J1939ClaimState.Claimed);

        // Owner reclaims on a different SA. The SendAsync path composes the 29-bit ID
        // from the currently-claimed SA on every tick, so the periodic emission naturally
        // resumes under the new SA without any explicit rebind.
        await owner.ClaimAddressAsync(secondSa).WithTimeout(ShortTimeout);
        owner.Address.Should().Be(secondSa);

        // Wait for a handful of frames under the new SA to confirm the schedule resumed.
        var readyDeadline = DateTime.UtcNow + ShortTimeout;
        while (Volatile.Read(ref newSaStamps) < 3 && DateTime.UtcNow < readyDeadline)
            await Task.Delay(10);
        Volatile.Read(ref newSaStamps).Should().BeGreaterOrEqualTo(3,
            "the schedule must resume under the reclaimed SA so downstream ECUs continue " +
            "to observe the PGN under the new (correct) source address");
    }
}

internal static class J1939NodeTestExtensions
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
