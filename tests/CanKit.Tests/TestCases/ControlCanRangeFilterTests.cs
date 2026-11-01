#if FAKE
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// A range filter reaches the driver as one VCI_FILTER_RECORD per rule, passed by pointer to
/// VCI_SetReference(RefType = 1). The fake reads the record the way the vendor library does, so
/// this covers the record layout and the pointer the bus hands over.
/// </summary>
public class ControlCanRangeFilterTests
{
    private const string Rx = "controlcan://VCI_USBCAN_2E_U?index=0#ch0";
    private const string Tx = "controlcan://VCI_USBCAN_2E_U?index=0#ch1";

    [Fact]
    public async Task Standard_Range_Filter_Record_Reaches_The_Driver()
    {
        using var rx = CanBus.Open(Rx, cfg => Configure(cfg).RangeFilter(0x100, 0x1FF, CanFilterIDType.Standard));
        using var tx = CanBus.Open(Tx, cfg => Configure(cfg));

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x0FF, new byte[] { 1 }),
            CanFrame.Classic(0x100, new byte[] { 2 }),
            CanFrame.Classic(0x1FF, new byte[] { 3 }),
            CanFrame.Classic(0x200, new byte[] { 4 }),
            CanFrame.Classic(0x150, new byte[] { 5 }, isExtendedFrame: true),
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => (d.CanFrame.ID, d.CanFrame.IsExtendedFrame))
            .Should().BeEquivalentTo(new[] { (0x100, false), (0x1FF, false) });
    }

    [Fact]
    public async Task Extended_Range_Filter_Record_Reaches_The_Driver()
    {
        using var rx = CanBus.Open(Rx, cfg => Configure(cfg).RangeFilter(0x18DA0000, 0x18DAFFFF, CanFilterIDType.Extend));
        using var tx = CanBus.Open(Tx, cfg => Configure(cfg));

        tx.Transmit(new[]
        {
            CanFrame.Classic(0x18D9FFFF, new byte[] { 1 }, isExtendedFrame: true),
            CanFrame.Classic(0x18DA00F1, new byte[] { 2 }, isExtendedFrame: true),
            CanFrame.Classic(0x18DB0000, new byte[] { 3 }, isExtendedFrame: true),
            CanFrame.Classic(0x100, new byte[] { 4 }),
        });

        var got = await rx.ReceiveAsync(8, 300, TestContext.Current.CancellationToken);
        got.Select(d => (d.CanFrame.ID, d.CanFrame.IsExtendedFrame))
            .Should().BeEquivalentTo(new[] { (0x18DA00F1, true) });
    }

    private static IBusInitOptionsConfigurator Configure(IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
}
#endif
