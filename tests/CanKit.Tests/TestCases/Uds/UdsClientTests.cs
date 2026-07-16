using System;
using System.Collections.Generic;
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
        Action<SimulatedUdsEcu> configure, UdsClientOptions? options = null)
    {
        var session = NewSession();
        var busClient = OpenClassic(session, 0);
        var busEcu = OpenClassic(session, 1);

        var clientEndpoint = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
        var ecuEndpoint = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);

        var clientChannel = IsoTpFactory.Open(busClient, clientEndpoint, FastIsoTp());
        var ecuChannel = IsoTpFactory.Open(busEcu, ecuEndpoint, FastIsoTp());

        var ecu = new SimulatedUdsEcu(ecuChannel);
        configure(ecu);
        ecu.Start();

        var client = UdsClient.Create(clientChannel, options);

        var dispose = new CompositeDisposable(client, ecu, ecuChannel, clientChannel, busEcu, busClient);
        return (client, ecu, dispose);
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
    // FR-UDS-005 — ECUReset (0x11).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task EcuReset_Completes_Before_Ecu_Would_Reboot()
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x11, req =>
        {
            req[0].Should().Be(0x11);
            req[1].Should().Be((byte)UdsEcuResetType.HardReset);
            return new byte[] { (byte)UdsEcuResetType.HardReset };
        }));
        using (dispose)
        {
            var tail = await client.EcuResetAsync(UdsEcuResetType.HardReset,
                new CancellationTokenSource(ShortTimeout).Token);
            tail.Should().BeEmpty();
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
            var results = await client.ReadDataByIdentifierAsync(
                new ushort[] { 0xF190, 0xF187, 0xF189 },
                new CancellationTokenSource(ShortTimeout).Token);

            results.Should().ContainKey((ushort)0xF190).WhoseValue.Should().Equal(0x41, 0x42, 0x43);
            results.Should().ContainKey((ushort)0xF187).WhoseValue.Should().Equal(0x11, 0x22);
            results.Should().ContainKey((ushort)0xF189).WhoseValue.Should().Equal(0x99);
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
