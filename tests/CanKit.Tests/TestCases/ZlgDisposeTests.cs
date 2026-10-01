#if FAKE
using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Disposing a ZLG bus stops its poll loop. That is a normal shutdown: it must not throw and
/// must not be reported as a fault, however the dispose and the poll loop interleave.
/// </summary>
public class ZlgDisposeTests
{
    [Theory]
    [InlineData("zlg://ZCAN_PCIE_CANFD_200U?index=0#ch0", "zlg://ZCAN_PCIE_CANFD_200U?index=0#ch1")]
    [InlineData("zlg://ZCAN_USBCANFD_200U?index=0#ch0", "zlg://ZCAN_USBCANFD_200U?index=0#ch1")]
    public void Dispose_While_Receiving_Does_Not_Throw_And_Does_Not_Report_A_Fault(string rxEndpoint, string txEndpoint)
    {
        var reported = new List<Exception>();
        var frames = Enumerable.Range(0, 64)
            .Select(i => CanFrame.Classic(0x100 + i, new byte[] { (byte)i }))
            .ToArray();

        // The interleaving is timing dependent, so repeat it. The burst keeps the poll loop busy
        // while the bus is disposed.
        for (var i = 0; i < 200; i++)
        {
            var rx = CanBus.Open(rxEndpoint, Configure);
            var tx = CanBus.Open(txEndpoint, Configure);
            rx.FaultOccurred += (_, ex) => { lock (reported) reported.Add(ex); };
            rx.BackgroundExceptionOccurred += (_, ex) => { lock (reported) reported.Add(ex); };

            tx.Transmit(frames);

            var disposeRx = () => rx.Dispose();
            disposeRx.Should().NotThrow();
            tx.Dispose();
        }

        lock (reported) reported.Should().BeEmpty();
    }

    private static void Configure(CanKit.Abstractions.API.Common.IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
}
#endif
