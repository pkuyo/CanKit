using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.IsoTp;
using CanKit.Tests.Utils;
using FluentAssertions;
using Xunit;
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.IsoTp;

/// <summary>
/// FR-RAW-051 verification: a simulated BusOff state must abort an active L3 (ISO-TP) send
/// with a defined, observable error instead of letting it hang until a protocol timeout.
/// </summary>
public class IsoTpBusOffTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(5);

    private static string NewSession() => $"isotp-busoff-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    // Echo-capable bus whose echo (and any other delivery) is blocked by the software filter:
    // the FF's SendConfirmed stays pending, which opens the deterministic window in which the
    // BusOff transition must resolve the confirmation (same trick as TxConfirmTests).
    private static ICanBus OpenEchoThatNeverArrives(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20)
            .Baud(TestCaseProvider.AbitRate)
            .SetWorkMode(ChannelWorkMode.Echo)
            .RangeFilter(0x001, 0x002, CanFilterIDType.Standard));

    private static IsoTpChannelOptions FastOptions()
        => new()
        {
            UseCanFd = false,
            UsePadding = true,
            LocalBlockSize = 0,
            LocalStMin = TimeSpan.Zero,
            // Long protocol timers: if the send faults quickly, it must be the BusOff path,
            // not N_As/N_Bs expiring.
            NAs = TimeSpan.FromSeconds(10),
            NBs = TimeSpan.FromSeconds(10),
            NCr = TimeSpan.FromSeconds(10),
            WftMax = 10,
        };

    [Fact]
    public async Task Active_MultiFrame_Send_Faults_With_BusOff_Instead_Of_Hanging()
    {
        var session = NewSession();
        using var busA = OpenEchoThatNeverArrives(session, 0);
        using var busB = Open(session, 1); // hub peer, deliberately no channel

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x300, 0x301), FastOptions());

        // Multi-frame send: the FF goes out and its TX confirmation stays pending behind the
        // blocked echo. Driving the bus off while that confirmation is outstanding must abort
        // the send (L2 -> L3 propagation per FR-RAW-051), not hang.
        var send = sender.SendAsync(Enumerable.Range(0, 30).Select(i => (byte)i).ToArray());

        // Give the channel a moment to register the pending FF confirmation.
        await Task.Delay(100);
        VirtualBusControl.DriveBusState(session, BusState.BusOff);
        VirtualBusControl.DriveFault(busA, new InvalidOperationException("simulated bus-off"));

        var sw = Stopwatch.StartNew();
        Func<Task> act = async () =>
        {
            var completed = await Task.WhenAny(send, Task.Delay(ShortTimeout));
            if (completed != send) throw new TimeoutException("SendAsync hung past the test bound.");
            await send;
        };
        var ex = (await act.Should().ThrowAsync<IsoTpException>()).Which;
        sw.Stop();

        ex.Message.Should().Contain("BusOff",
            "the failed TX confirmation (BusOff) must surface as an observable ISO-TP error");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the abort must be immediate, not via the (10 s) N_As/N_Bs protocol timers");
    }
}
