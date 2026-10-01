#if FAKE
using System;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ZLG.Transceivers;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// The merged receive path (ZCAN_ReceiveData) is used for PCIe-CANFD devices. It reads from a
/// device-wide queue, so a channel can be asked for more frames than it will ever get.
/// </summary>
public class ZlgMergeReceiveTests
{
    private const string Endpoint = "zlg://ZCAN_PCIE_CANFD_200U?index=0#ch0";

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public async Task Receive_Returns_When_Fewer_Frames_Arrive_Than_Requested(int timeOutMs)
    {
        using var bus = CanBus.Open(Endpoint,
            cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
        var transceiver = new ZlgCanMergeTransceiver();

        // Nothing is on the bus, so the five requested frames never arrive.
        var receive = Task.Run(() =>
            transceiver.Receive((ICanBus<IBusRTOptionsConfigurator>)bus, 5, timeOutMs).ToList());

        var finished = await Task.WhenAny(receive,
            Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        finished.Should().BeSameAs(receive, "the receive call must return instead of spinning");
        (await receive).Should().BeEmpty();
    }
}
#endif
