using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.IsoTp;
using CanKit.Pro.Uds;
using FluentAssertions;
using Xunit;
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.Uds;

/// <summary>
/// End-to-end integration tests for <see cref="IUdsClient"/> against a
/// <see cref="SimulatedUdsEcu"/> running over two ISO-TP channels on the Virtual bus.
/// Covers SRS FR-UDS-001..010 plus the FR-UDS-011 SHOULD (multi-DID read).
/// </summary>
public class UdsClientTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

    private static string NewSession() => $"uds-{Guid.NewGuid():N}";

    private static ICanBus OpenClassic(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static IsoTpChannelOptions FastIsoTp() => new()
    {
        UseCanFd = false,
        UsePadding = true,
        NAs = TimeSpan.FromMilliseconds(500),
        NBs = TimeSpan.FromMilliseconds(500),
        NCr = TimeSpan.FromMilliseconds(500),
    };

    /// <summary>
    /// Builds a client ↔ ECU pair over a fresh Virtual session, wires the ECU handlers via
    /// <paramref name="configure"/>, and returns everything the test needs to drive the client.
    /// The returned <see cref="IDisposable"/> tears down the whole stack in the right order.
    /// </summary>
    private static (IUdsClient client, SimulatedUdsEcu ecu, IDisposable dispose) BuildPair(
        Action<SimulatedUdsEcu> configure, UdsClientOptions? options = null, bool useCanFd = false)
    {
        var session = NewSession();
        var busClient = useCanFd ? OpenCanFd(session, 0) : OpenClassic(session, 0);
        var busEcu = useCanFd ? OpenCanFd(session, 1) : OpenClassic(session, 1);

        var clientEndpoint = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
        var ecuEndpoint = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);

        var clientChannel = IsoTpFactory.Open(busClient, clientEndpoint, FastIsoTp(useCanFd));
        var ecuChannel = IsoTpFactory.Open(busEcu, ecuEndpoint, FastIsoTp(useCanFd));

        var ecu = new SimulatedUdsEcu(ecuChannel);
        configure(ecu);
        // Start() blocks until the ECU receive loop is subscribed (no fixed Sleep race).
        ecu.Start();

        var client = UdsClient.Create(clientChannel, options);

        var dispose = new CompositeDisposable(client, ecu, ecuChannel, clientChannel, busEcu, busClient);
        return (client, ecu, dispose);
    }

    private static ICanBus OpenCanFd(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.CanFd).Fd(TestCaseProvider.AbitRate, TestCaseProvider.DbitRate));

    private static IsoTpChannelOptions FastIsoTp(bool useCanFd) => new()
    {
        UseCanFd = useCanFd,
        UsePadding = true,
        NAs = TimeSpan.FromMilliseconds(500),
        NBs = TimeSpan.FromMilliseconds(500),
        NCr = TimeSpan.FromMilliseconds(500),
    };

    // -----------------------------------------------------------------------------------
    // CAN-FD matrix (FR-UDS over ISO-TP on CAN FD): the core diagnostic flows must behave
    // identically when the transport runs on CAN-FD frames instead of classic CAN.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task CanFd_DiagnosticSessionControl_And_ReadDid_Work_Like_On_Classic()
    {
        var (client, ecu, dispose) = BuildPair(e =>
        {
            e.On(0x10, req => new byte[] { req[1], 0x00, 0x32, 0x01, 0xF4 });
            e.On(0x22, req => new byte[] { req[1], req[2], 0x57, 0x42, 0x41 });
        }, useCanFd: true);
        using (dispose)
        {
            var sessionResponse = await client.DiagnosticSessionControlAsync(UdsSessionType.Extended,
                new CancellationTokenSource(ShortTimeout).Token);
            client.CurrentSession.Should().Be((byte)UdsSessionType.Extended);

            var data = await client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            data.Should().Equal(0x57, 0x42, 0x41);
        }
    }

    [Fact]
    public async Task CanFd_WriteDid_And_MultiFrame_Transfer_Work_Like_On_Classic()
    {
        var written = new List<byte>();
        var (client, ecu, dispose) = BuildPair(e => e.On(0x2E, req =>
        {
            written.Clear();
            written.AddRange(req);
            return new byte[] { req[1], req[2] };
        }), useCanFd: true);
        using (dispose)
        {
            // 300-byte DID forces a multi-frame ISO-TP transfer on both directions.
            var payload = Enumerable.Range(0, 300).Select(i => (byte)(i & 0xFF)).ToArray();
            await client.WriteDataByIdentifierAsync(0xF190, payload,
                new CancellationTokenSource(ShortTimeout).Token);

            written.Should().HaveCount(303, "0x2E + DID (2) + 300 payload bytes must arrive intact over CAN FD");
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-001 — DiagnosticSessionControl (0x10): tester requests Extended session,
    // ECU replies with the session parameter record; client tracks CurrentSession.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task DiagnosticSessionControl_Switches_Session_And_Returns_Parameter_Record()
    {
        var (client, ecu, dispose) = BuildPair(e => e.On(0x10, req =>
        {
            req.Should().Equal(0x10, (byte)UdsSessionType.Extended);
            return new byte[] { (byte)UdsSessionType.Extended, 0x00, 0x32, 0x01, 0xF4 };
        }));
        using (dispose)
        {
            client.CurrentSession.Should().Be((byte)UdsSessionType.Default);
            var record = await client.DiagnosticSessionControlAsync(UdsSessionType.Extended,
                new CancellationTokenSource(ShortTimeout).Token);

            record.Should().Equal(0x00, 0x32, 0x01, 0xF4);
            client.CurrentSession.Should().Be((byte)UdsSessionType.Extended);
            ecu.RequestsHandled.Should().Be(1);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-002 — ReadDataByIdentifier (0x22) single DID.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task ReadDataByIdentifier_Returns_Data_Record()
    {
        byte[] vin = System.Text.Encoding.ASCII.GetBytes("WBADT43452G296403"); // 17 bytes
        var (client, _, dispose) = BuildPair(e => e.On(0x22, req =>
        {
            req.Length.Should().Be(3);
            req[0].Should().Be(0x22);
            var did = (ushort)((req[1] << 8) | req[2]);
            did.Should().Be(0xF190);

            var body = new byte[2 + vin.Length];
            body[0] = 0xF1; body[1] = 0x90;
            Buffer.BlockCopy(vin, 0, body, 2, vin.Length);
            return body;
        }));
        using (dispose)
        {
            var data = await client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            data.Should().Equal(vin);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-003 — WriteDataByIdentifier (0x2E).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task WriteDataByIdentifier_Completes_On_Positive_Response()
    {
        byte[] written = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        byte[]? received = null;
        var (client, _, dispose) = BuildPair(e => e.On(0x2E, req =>
        {
            req[0].Should().Be(0x2E);
            var did = (ushort)((req[1] << 8) | req[2]);
            did.Should().Be(0xF200);
            received = req.AsSpan(3).ToArray();
            return new byte[] { 0xF2, 0x00 };
        }));
        using (dispose)
        {
            await client.WriteDataByIdentifierAsync(0xF200, written,
                new CancellationTokenSource(ShortTimeout).Token);
            received.Should().Equal(written);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-004 — RoutineControl (0x31): exercise all three sub-functions.
    // -----------------------------------------------------------------------------------
    [Theory]
    [InlineData(UdsRoutineControlType.StartRoutine)]
    [InlineData(UdsRoutineControlType.StopRoutine)]
    [InlineData(UdsRoutineControlType.RequestRoutineResults)]
    public async Task RoutineControl_Round_Trips_All_Three_SubFunctions(
        UdsRoutineControlType sub)
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x31, req =>
        {
            req[0].Should().Be(0x31);
            req[1].Should().Be((byte)sub);
            var rid = (ushort)((req[2] << 8) | req[3]);
            rid.Should().Be(0x0203);
            return new byte[] { (byte)sub, 0x02, 0x03, 0xAB, 0xCD };
        }));
        using (dispose)
        {
            var info = await client.RoutineControlAsync(sub, 0x0203,
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);
            info.Should().Equal(0xAB, 0xCD);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-005 — ECUReset (0x11). After a successful reset the ECU returns to the
    // default session, so CurrentSession must not keep a stale DiagnosticSessionControl
    // value (Bugbot 3597974544).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task EcuReset_Completes_Before_Ecu_Would_Reboot()
    {
        var (client, _, dispose) = BuildPair(e =>
        {
            e.On(0x10, req =>
            {
                req.Should().Equal(0x10, (byte)UdsSessionType.Extended);
                return new byte[] { (byte)UdsSessionType.Extended, 0x00, 0x32, 0x01, 0xF4 };
            });
            e.On(0x11, req =>
            {
                req[0].Should().Be(0x11);
                req[1].Should().Be((byte)UdsEcuResetType.HardReset);
                return new byte[] { (byte)UdsEcuResetType.HardReset };
            });
        });
        using (dispose)
        {
            await client.DiagnosticSessionControlAsync(UdsSessionType.Extended,
                new CancellationTokenSource(ShortTimeout).Token);
            client.CurrentSession.Should().Be((byte)UdsSessionType.Extended);

            var tail = await client.EcuResetAsync(UdsEcuResetType.HardReset,
                new CancellationTokenSource(ShortTimeout).Token);
            tail.Should().BeEmpty();
            client.CurrentSession.Should().Be((byte)UdsSessionType.Default);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-006 — SecurityAccess (0x27): seed → caller-computed key → sendKey.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task SecurityAccess_Sends_ComputedKey_And_Accepts_Unlock()
    {
        byte[] seed = { 0xDE, 0xAD, 0xBE, 0xEF };
        byte[]? sentKey = null;
        bool unlocked = false;

        var (client, _, dispose) = BuildPair(e => e
            .On(0x27, req =>
            {
                req[0].Should().Be(0x27);
                var sub = req[1];
                if (sub == 0x01) // requestSeed
                {
                    var body = new byte[1 + seed.Length];
                    body[0] = 0x01;
                    Buffer.BlockCopy(seed, 0, body, 1, seed.Length);
                    return body;
                }
                if (sub == 0x02) // sendKey
                {
                    sentKey = req.AsSpan(2).ToArray();
                    unlocked = true;
                    return new byte[] { 0x02 };
                }
                throw new EcuNegativeResponse(0x12); // subFunctionNotSupported
            }));

        using (dispose)
        {
            await client.SecurityAccessAsync(
                requestSeedLevel: 0x01,
                computeKey: s => s.Select(b => (byte)(b ^ 0x55)).ToArray(),
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);

            unlocked.Should().BeTrue();
            sentKey.Should().Equal(seed.Select(b => (byte)(b ^ 0x55)).ToArray());
        }
    }

    // -----------------------------------------------------------------------------------
    // SecurityAccess must hold the request lock across seed + sendKey so TesterPresent
    // keep-alive cannot interleave and provoke NRC requestSequenceError on real ECUs.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task SecurityAccess_Holds_Lock_Across_Seed_And_Key()
    {
        var wireOrder = new List<byte>();
        var orderGate = new object();
        byte[] seed = { 0x01, 0x02, 0x03, 0x04 };
        var keyStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseKey = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var (client, _, dispose) = BuildPair(e => e
            .On(0x27, req =>
            {
                lock (orderGate) wireOrder.Add(req[1]);
                if (req[1] == 0x01)
                {
                    var body = new byte[1 + seed.Length];
                    body[0] = 0x01;
                    Buffer.BlockCopy(seed, 0, body, 1, seed.Length);
                    return body;
                }
                if (req[1] == 0x02)
                    return new byte[] { 0x02 };
                throw new EcuNegativeResponse(0x12);
            })
            .On(0x3E, req =>
            {
                lock (orderGate) wireOrder.Add(0x3E);
                return Array.Empty<byte>();
            }),
            options: new UdsClientOptions
            {
                TesterPresentPeriod = TimeSpan.FromMilliseconds(30),
                KeepAliveSuppressPositiveResponse = true,
            });

        using (dispose)
        {
            using var keepAlive = client.StartTesterPresentKeepAlive(TimeSpan.FromMilliseconds(30));

            var unlock = client.SecurityAccessAsync(
                requestSeedLevel: 0x01,
                computeKey: s =>
                {
                    keyStarted.TrySetResult(true);
                    // Block inside computeKey (still under the request lock) long enough that
                    // several keep-alive ticks fire; they must not transmit until unlock ends.
                    releaseKey.Task.Wait(ShortTimeout);
                    return s.Select(b => (byte)(b ^ 0x55)).ToArray();
                },
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);

            await keyStarted.Task.WaitAsync(ShortTimeout);
            await Task.Delay(120); // several keep-alive periods while lock is held
            releaseKey.TrySetResult(true);
            await unlock;

            lock (orderGate)
            {
                // Seed (0x01) then key (0x02) must be adjacent; 0x3E may appear only outside.
                int seedIdx = wireOrder.IndexOf(0x01);
                int keyIdx = wireOrder.IndexOf(0x02);
                seedIdx.Should().BeGreaterThanOrEqualTo(0);
                keyIdx.Should().Be(seedIdx + 1);
                wireOrder.Skip(seedIdx).Take(2).Should().Equal((byte)0x01, (byte)0x02);
            }
        }
    }

    // -----------------------------------------------------------------------------------
    // After P2 timeout, a late ECU reply must not be consumed as the next request's answer
    // when the next request uses the same service (SID correlation alone is insufficient).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task TimedOut_Request_Does_Not_Poison_Next_Same_Service_Transaction()
    {
        int calls = 0;
        var staleEnqueued = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, req =>
            {
                int n = Interlocked.Increment(ref calls);
                if (n == 1)
                {
                    // Arrive after the client's P2 budget so the first call times out.
                    Thread.Sleep(250);
                    return new byte[] { 0xF1, 0x90, 0xAA }; // stale payload
                }

                return new byte[] { 0xF1, 0x90, 0xBB }; // fresh payload
            }),
            options: new UdsClientOptions
            {
                P2ClientMax = TimeSpan.FromMilliseconds(80),
                P2StarClientMax = TimeSpan.FromMilliseconds(80),
            });

        using (dispose)
        {
            client.Channel.DatagramReceived += (_, args) =>
            {
                // Positive RDBI response carrying the stale 0xAA data record.
                if (args.Data.Length >= 4 && args.Data[0] == 0x62 && args.Data[3] == 0xAA)
                    staleEnqueued.TrySetResult(true);
            };

            Func<Task> first = () => client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            await first.Should().ThrowAsync<UdsTimeoutException>();

            await staleEnqueued.Task.WaitAsync(ShortTimeout);

            var data = await client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            data.Should().Equal(0xBB);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-007 — TesterPresent (0x3E): keep-alive fires periodically without blocking.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task TesterPresent_KeepAlive_Fires_Periodically()
    {
        int count = 0;
        var handled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (client, ecu, dispose) = BuildPair(e => e.On(0x3E, req =>
        {
            req[0].Should().Be(0x3E);
            (req[1] & 0x80).Should().Be(0x80, "keep-alive must suppress positive response");
            int c = Interlocked.Increment(ref count);
            if (c >= 3) handled.TrySetResult(true);
            return Array.Empty<byte>();
        }),
        options: new UdsClientOptions { TesterPresentPeriod = TimeSpan.FromMilliseconds(80) });

        using (dispose)
        {
            using (var handle = client.StartTesterPresentKeepAlive())
            {
                await handled.Task.WaitAsync(ShortTimeout);
            }
            count.Should().BeGreaterThanOrEqualTo(3);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-008 — P2 timeout: ECU silent -> client faults with UdsTimeoutException(P2).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Client_Times_Out_When_Ecu_Silent_Within_P2()
    {
        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, _ => throw new EcuSilent()),
            options: new UdsClientOptions
            {
                P2ClientMax = TimeSpan.FromMilliseconds(150),
                P2StarClientMax = TimeSpan.FromMilliseconds(150),
            });
        using (dispose)
        {
            Func<Task> act = () => client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            var ex = (await act.Should().ThrowAsync<UdsTimeoutException>()).Which;
            ex.Timer.Should().Be(UdsTimeoutTimer.P2);
            ex.RequestedService.Should().Be(UdsServiceId.ReadDataByIdentifier);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-008 — P2* timeout: ECU sends only NRC 0x78 (response pending), never a final
    // response -> the restarted P2* timer must expire and fault with
    // UdsTimeoutException(Timer = P2Star). This path had no test at all before (the only
    // Timer assertion in the suite was P2); the elapsed-time assertion proves the timeout
    // fired on the restarted P2* budget, not on the initial P2 budget.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Client_Times_Out_With_P2Star_When_Ecu_Sends_Only_ResponsePending()
    {
        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, _ => throw new EcuResponsePendingThenSilent(
                pendingCount: 2, delayBetween: TimeSpan.FromMilliseconds(40))),
            options: new UdsClientOptions
            {
                P2ClientMax = TimeSpan.FromMilliseconds(100),
                P2StarClientMax = TimeSpan.FromMilliseconds(250),
                MaxResponsePendingCount = 10,
            });
        using (dispose)
        {
            var sw = Stopwatch.StartNew();
            Func<Task> act = () => client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            var ex = (await act.Should().ThrowAsync<UdsTimeoutException>()).Which;
            sw.Stop();

            ex.Timer.Should().Be(UdsTimeoutTimer.P2Star,
                "the ECU answered with NRC 0x78 (response pending), so the running timer is P2* — not P2");
            ex.RequestedService.Should().Be(UdsServiceId.ReadDataByIdentifier);
            sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(200),
                "P2* restarts on each 0x78, so the timeout must fire well past the initial P2 budget (100 ms)");
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-009 — NRC 0x78 responsePending: ECU sends 3× 0x78 then final response;
    // client MUST wait inside P2* and deliver the final response.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task ResponsePending_Restarts_P2Star_And_Returns_Final_Response()
    {
        byte[] finalBody = { 0xF1, 0x90, 0xAA, 0xBB, 0xCC, 0xDD };
        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, _ => throw new EcuResponsePending(
                pendingCount: 3, finalResponse: finalBody,
                delayBetween: TimeSpan.FromMilliseconds(60))),
            options: new UdsClientOptions
            {
                // P2 short: proves the client actually restarts on 0x78 rather than living
                // inside the (accidentally) generous initial budget.
                P2ClientMax = TimeSpan.FromMilliseconds(120),
                P2StarClientMax = TimeSpan.FromSeconds(1),
                MaxResponsePendingCount = 10,
            });
        using (dispose)
        {
            var data = await client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            data.Should().Equal(0xAA, 0xBB, 0xCC, 0xDD);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-009 (edge) — MaxResponsePendingCount bounds the loop.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task ResponsePending_Loop_Aborts_When_Exceeding_MaxResponsePendingCount()
    {
        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, _ => throw new EcuResponsePending(
                pendingCount: 5, finalResponse: new byte[] { 0xF1, 0x90 },
                delayBetween: TimeSpan.FromMilliseconds(20))),
            options: new UdsClientOptions
            {
                P2ClientMax = TimeSpan.FromMilliseconds(500),
                P2StarClientMax = TimeSpan.FromSeconds(1),
                MaxResponsePendingCount = 2,
            });
        using (dispose)
        {
            Func<Task> act = () => client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            await act.Should().ThrowAsync<UdsProtocolException>()
                .WithMessage("*NRC 0x78*MaxResponsePendingCount=2*");
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-010 — structured NRC: ECU returns 0x31 (requestOutOfRange) for RDBI.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Negative_Response_Is_Surfaced_As_Structured_Exception()
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x22, _ => throw new EcuNegativeResponse(0x31)));
        using (dispose)
        {
            Func<Task> act = () => client.ReadDataByIdentifierAsync(0x1234,
                new CancellationTokenSource(ShortTimeout).Token);
            var ex = (await act.Should().ThrowAsync<UdsNegativeResponseException>()).Which;
            ex.RequestedService.Should().Be(UdsServiceId.ReadDataByIdentifier);
            ex.Code.Should().Be(0x31);
            ex.CodeAsEnum.Should().Be(UdsNegativeResponseCode.RequestOutOfRange);
            ex.CodeName.Should().Be("RequestOutOfRange");
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-011 (SHOULD) — multi-DID ReadDataByIdentifier.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task ReadDataByIdentifier_Multi_Did_Splits_Records()
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x22, req =>
        {
            // Request layout: [0]=0x22 [1..]=DIDs (3× 2 bytes)
            req.Length.Should().Be(1 + 3 * 2);
            // Response layout: DID + record for each requested DID, concatenated after SID+0x40.
            //   0xF1 0x90 (VIN, 3 bytes for brevity)
            //   0xF1 0x87 (2 bytes)
            //   0xF1 0x89 (1 byte)
            return new byte[]
            {
                0xF1, 0x90, 0x41, 0x42, 0x43,
                0xF1, 0x87, 0x11, 0x22,
                0xF1, 0x89, 0x99,
            };
        }));
        using (dispose)
        {
            var lengths = new Dictionary<ushort, int>
            {
                [(ushort)0xF190] = 3,
                [(ushort)0xF187] = 2,
                [(ushort)0xF189] = 1,
            };
            var results = await client.ReadDataByIdentifierAsync(
                new ushort[] { 0xF190, 0xF187, 0xF189 },
                lengths,
                new CancellationTokenSource(ShortTimeout).Token);

            results.Should().ContainKey((ushort)0xF190).WhoseValue.Should().Equal(0x41, 0x42, 0x43);
            results.Should().ContainKey((ushort)0xF187).WhoseValue.Should().Equal(0x11, 0x22);
            results.Should().ContainKey((ushort)0xF189).WhoseValue.Should().Equal(0x99);
        }
    }

    // Bugbot 3596522007 — ISO 14229-1 allows empty dataRecord; adjacent DIDs must not throw.
    [Fact]
    public async Task ReadDataByIdentifier_Multi_Did_Accepts_Empty_Records()
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x22, _ => new byte[]
        {
            // DID 0xF190 with empty record, then DID 0xF187 with 2 bytes, then DID 0xF189 empty.
            0xF1, 0x90,
            0xF1, 0x87, 0x11, 0x22,
            0xF1, 0x89,
        }));
        using (dispose)
        {
            var lengths = new Dictionary<ushort, int>
            {
                [(ushort)0xF190] = 0,
                [(ushort)0xF187] = 2,
                [(ushort)0xF189] = 0,
            };
            var results = await client.ReadDataByIdentifierAsync(
                new ushort[] { 0xF190, 0xF187, 0xF189 },
                lengths,
                new CancellationTokenSource(ShortTimeout).Token);

            results.Should().ContainKey((ushort)0xF190).WhoseValue.Should().BeEmpty();
            results.Should().ContainKey((ushort)0xF187).WhoseValue.Should().Equal(0x11, 0x22);
            results.Should().ContainKey((ushort)0xF189).WhoseValue.Should().BeEmpty();
        }
    }

    // Bugbot 3596550854 — payload bytes matching a later DID must not become record boundaries.
    [Fact]
    public async Task ReadDataByIdentifier_Multi_Did_Does_Not_Split_On_Data_Bytes()
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x22, _ => new byte[]
        {
            // DID 0x1234 data contains the byte pair of the next DID (0xF187); length-based
            // parsing must keep those bytes inside 0x1234's record.
            0x12, 0x34, 0xF1, 0x87, 0xAA,
            0xF1, 0x87, 0x11, 0x22,
        }));
        using (dispose)
        {
            var lengths = new Dictionary<ushort, int>
            {
                [(ushort)0x1234] = 3,
                [(ushort)0xF187] = 2,
            };
            var results = await client.ReadDataByIdentifierAsync(
                new ushort[] { 0x1234, 0xF187 },
                lengths,
                new CancellationTokenSource(ShortTimeout).Token);

            results.Should().ContainKey((ushort)0x1234).WhoseValue.Should().Equal(0xF1, 0x87, 0xAA);
            results.Should().ContainKey((ushort)0xF187).WhoseValue.Should().Equal(0x11, 0x22);
        }
    }

    // -----------------------------------------------------------------------------------
    // Sanity: unknown service still surfaces the ECU's serviceNotSupported NRC (defense in
    // depth for FR-UDS-010 mapping of arbitrary NRC bytes).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Unknown_Service_Response_Is_Surfaced_As_ServiceNotSupported()
    {
        // No handlers configured — the ECU returns 0x11 for anything it sees.
        var (client, _, dispose) = BuildPair(_ => { });
        using (dispose)
        {
            Func<Task> act = () => client.DiagnosticSessionControlAsync(UdsSessionType.Extended,
                new CancellationTokenSource(ShortTimeout).Token);
            var ex = (await act.Should().ThrowAsync<UdsNegativeResponseException>()).Which;
            ex.Code.Should().Be(0x11);
            ex.CodeAsEnum.Should().Be(UdsNegativeResponseCode.ServiceNotSupported);
        }
    }

    // -----------------------------------------------------------------------------------
    // Bugbot 3596444327 — Dispose must cancel the lifetime token and wait for an in-flight
    // ExecuteAsync to Release _requestLock before disposing the semaphore.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Dispose_During_InFlight_Request_Does_Not_Race_RequestLock()
    {
        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, _ => throw new EcuSilent()),
            options: new UdsClientOptions
            {
                P2ClientMax = TimeSpan.FromSeconds(5),
                P2StarClientMax = TimeSpan.FromSeconds(5),
            });
        try
        {
            var inFlight = client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            await Task.Delay(50); // enter ReceiveWithTimeout under the request lock

            Action act = () => client.Dispose();
            act.Should().NotThrow(
                "Dispose must wait for the in-flight request to Release before disposing _requestLock");

            Func<Task> wait = () => inFlight;
            await wait.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            dispose.Dispose();
        }
    }

    // -----------------------------------------------------------------------------------
    // Bugbot 3596586770 — suppress-positive TesterPresentAsync must honor _lifetimeCts so
    // Dispose cancels a call blocked on _requestLock (or about to Send) instead of letting
    // it transmit after teardown.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Dispose_Cancels_Suppress_TesterPresent_Blocked_On_RequestLock()
    {
        var (client, _, dispose) = BuildPair(
            e => e.On(0x22, _ => throw new EcuSilent()),
            options: new UdsClientOptions
            {
                P2ClientMax = TimeSpan.FromSeconds(5),
                P2StarClientMax = TimeSpan.FromSeconds(5),
            });
        try
        {
            // Hold the request lock with a silent ECU read so suppress TesterPresent blocks
            // in WaitAsync rather than racing through Send.
            var inFlight = client.ReadDataByIdentifierAsync(0xF190,
                new CancellationTokenSource(ShortTimeout).Token);
            await Task.Delay(50);

            var testerPresent = client.TesterPresentAsync(suppressPositiveResponse: true,
                new CancellationTokenSource(ShortTimeout).Token);
            await Task.Delay(30); // park on _requestLock.WaitAsync

            Action act = () => client.Dispose();
            act.Should().NotThrow();

            Func<Task> waitTp = () => testerPresent;
            await waitTp.Should().ThrowAsync<OperationCanceledException>(
                "suppress TesterPresent must link _lifetimeCts so Dispose aborts WaitAsync/Send");

            Func<Task> waitRead = () => inFlight;
            await waitRead.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            dispose.Dispose();
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _items;
        public CompositeDisposable(params IDisposable[] items) => _items = items;
        public void Dispose()
        {
            foreach (var d in _items)
            {
                try { d.Dispose(); } catch { /* ignored */ }
            }
        }
    }
}
