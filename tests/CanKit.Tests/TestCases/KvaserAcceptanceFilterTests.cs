#if FAKE
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Checks the exact set of frames a Kvaser acceptance filter lets through. The generic filter
/// matrix only waits until the expected number of frames has arrived, so it cannot see a
/// filter that accepts too much. The FAKE CANlib shares the canAccept sequence with the real
/// binding, which makes these tests cover the code/mask/flag handling used on hardware.
/// </summary>
public class KvaserAcceptanceFilterTests
{
    private const string RxEndpoint = "kvaser://0";
    private const string TxEndpoint = "kvaser://1";

    [Fact]
    public async Task Standard_Mask_Filter_Rejects_Standard_Ids_Outside_The_Mask()
    {
        var received = await SendThroughFilterAsync(
            cfg => cfg.AccMask(0x060, 0x7F0, CanFilterIDType.Standard),
            Std(0x060), Std(0x061), Std(0x160), Std(0x1E0), Ext(0x18FF50E5));

        // 0x160 and 0x1E0 only differ from the code in bits covered by the mask.
        received.Should().Equal((0x060, false), (0x061, false), (0x18FF50E5, true));
    }

    [Fact]
    public async Task Extended_Mask_Filter_Rejects_Extended_Ids_Outside_The_Mask()
    {
        var received = await SendThroughFilterAsync(
            cfg => cfg.AccMask(0x18FF0000, 0x1FFF0000, CanFilterIDType.Extend),
            Std(0x060), Std(0x7FF), Ext(0x18FF50E5), Ext(0x1ABCDE01));

        // The extended filter must not touch standard frames.
        received.Should().Equal((0x060, false), (0x7FF, false), (0x18FF50E5, true));
    }

    [Fact]
    public async Task Standard_And_Extended_Mask_Filters_Apply_Independently()
    {
        var received = await SendThroughFilterAsync(
            cfg => cfg
                .AccMask(0x060, 0x7F0, CanFilterIDType.Standard)
                .AccMask(0x18FF0000, 0x1FFF0000, CanFilterIDType.Extend),
            Std(0x061), Std(0x160), Ext(0x18FF50E5), Ext(0x1ABCDE01));

        received.Should().Equal((0x061, false), (0x18FF50E5, true));
    }

    private static CanFrame Std(int id) => CanFrame.Classic(id, new[] { (byte)(id & 0xFF) });

    private static CanFrame Ext(int id) => CanFrame.Classic(id, new[] { (byte)(id & 0xFF) }, isExtendedFrame: true);

    private static async Task<List<(int Id, bool Extended)>> SendThroughFilterAsync(
        System.Action<CanKit.Abstractions.API.Common.IBusInitOptionsConfigurator> filter,
        params CanFrame[] frames)
    {
        var ct = TestContext.Current.CancellationToken;

        using var rx = CanBus.Open(RxEndpoint, cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
            filter(cfg);
            cfg.SetAsyncBufferCapacity(256);
        });
        // Opened directly so the test does not depend on CANKIT_TEST_ADAPTERS.
        using var tx = CanBus.Open(TxEndpoint,
            cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

        foreach (var frame in frames)
            tx.Transmit(frame).Should().Be(1);

        // Collect everything that arrives, not just an expected count.
        var received = await rx.ReceiveAsync(frames.Length + 8, 500, ct);
        return received.Select(r => (r.CanFrame.ID, r.CanFrame.IsExtendedFrame)).ToList();
    }
}
#endif
