#if FAKE
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Filter rules are combined with OR. The hardware takes the list only when it can express it as
/// a whole; any other list is evaluated in software with the hardware filter left open, so the
/// OR is preserved. Building that list must not modify the rule list being iterated.
/// </summary>
public class ControlCanFilterRulesTests
{
    private const string RangeRx = "controlcan://VCI_USBCAN_2E_U?index=0#ch0";
    private const string RangeTx = "controlcan://VCI_USBCAN_2E_U?index=0#ch1";
    private const string MaskRx = "controlcan://VCI_USBCAN2?index=0#ch0";
    private const string MaskTx = "controlcan://VCI_USBCAN2?index=0#ch1";

    [Fact]
    public async Task Range_And_Mask_Rules_Are_Evaluated_In_Software_As_A_Union()
    {
        using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard)
            .AccMask(0x400, 0x7F0, CanFilterIDType.Standard));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().HaveCount(2, "a mixed list cannot be split without losing the OR");

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x100, new byte[] { 1 }), // range only
            CanFrame.Classic(0x300, new byte[] { 2 }), // neither
            CanFrame.Classic(0x405, new byte[] { 3 }), // mask only
            CanFrame.Classic(0x1FF, new byte[] { 4 }), // range only
            CanFrame.Classic(0x4FF, new byte[] { 5 }), // neither
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x100, 0x405, 0x1FF);
    }

    [Fact]
    public async Task Range_Rules_Alone_Stay_In_Hardware()
    {
        using var rx = CanBus.Open(RangeRx, cfg => Configure(cfg)
            .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard)
            .RangeFilter(0x400, 0x40F, CanFilterIDType.Standard));
        using var tx = CanBus.Open(RangeTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().BeEmpty();

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x100, new byte[] { 1 }),
            CanFrame.Classic(0x300, new byte[] { 2 }),
            CanFrame.Classic(0x405, new byte[] { 3 }),
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x100, 0x405);
    }

    [Fact]
    public async Task Several_Mask_Rules_On_A_Mask_Device_Are_Evaluated_In_Software_As_A_Union()
    {
        using var rx = CanBus.Open(MaskRx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .AccMask(0x100, 0x7F0, CanFilterIDType.Standard)
            .AccMask(0x400, 0x7F0, CanFilterIDType.Standard));
        using var tx = CanBus.Open(MaskTx, cfg => Configure(cfg));

        rx.Options.Filter.SoftwareFilterRules.Should().HaveCount(2);

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x105, new byte[] { 1 }), // first mask
            CanFrame.Classic(0x205, new byte[] { 2 }), // neither
            CanFrame.Classic(0x405, new byte[] { 3 }), // second mask
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x105, 0x405);
    }

    private static IBusInitOptionsConfigurator Configure(IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
}
#endif
