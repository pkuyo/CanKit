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
/// On a range-filter device, rules the hardware does not take (mask rules) are applied in software.
/// Moving them must not modify the rule list that is being iterated.
/// </summary>
public class ControlCanFilterRulesTests
{
    private const string Rx = "controlcan://VCI_USBCAN_2E_U?index=0#ch0";
    private const string Tx = "controlcan://VCI_USBCAN_2E_U?index=0#ch1";

    [Fact]
    public async Task Range_Rule_Goes_To_Hardware_And_Mask_Rule_To_Software()
    {
        using var rx = CanBus.Open(Rx, cfg => Configure(cfg)
            .SoftwareFeaturesFallBack(CanFeature.MaskFilter)
            .RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard)
            .AccMask(0x100, 0x7F0, CanFilterIDType.Standard));
        using var tx = CanBus.Open(Tx, cfg => Configure(cfg));

        rx.Options.Filter.FilterRules.Should().HaveCount(2);
        rx.Options.Filter.SoftwareFilterRules.Should().ContainSingle(r => r is FilterRule.Mask);

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x100, new byte[] { 1 }), // in range, matches mask
            CanFrame.Classic(0x150, new byte[] { 2 }), // in range, rejected by the software mask
            CanFrame.Classic(0x300, new byte[] { 3 }), // outside the hardware range
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => d.CanFrame.ID).Should().Equal(0x100);
    }

    private static IBusInitOptionsConfigurator Configure(IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
}
#endif
