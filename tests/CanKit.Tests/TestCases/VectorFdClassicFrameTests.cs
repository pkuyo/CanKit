#if FAKE
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// A CAN FD bus carries classic frames as well; the Vector FD transceiver has to accept both.
/// Runs against the FAKE XL driver.
/// </summary>
public class VectorFdClassicFrameTests
{
    private const string EndpointA = "vector://virtual/0";
    private const string EndpointB = "vector://virtual/1";

    [Fact]
    public async Task Classic_Frame_Can_Be_Sent_On_An_Fd_Bus()
    {
        using var rx = CanBus.Open(EndpointA, ConfigureFd);
        using var tx = CanBus.Open(EndpointB, ConfigureFd);

        tx.Transmit(CanFrame.Classic(0x123, new byte[] { 1, 2, 3 })).Should().Be(1);

        var received = await rx.ReceiveAsync(1, 2000, TestContext.Current.CancellationToken);
        var frame = received.Should().ContainSingle().Which.CanFrame;
        frame.FrameKind.Should().Be(CanFrameType.Can20);
        frame.ID.Should().Be(0x123);
        frame.Data.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Classic_And_Fd_Frames_Can_Be_Mixed_In_One_Batch()
    {
        using var rx = CanBus.Open(EndpointA, ConfigureFd);
        using var tx = CanBus.Open(EndpointB, ConfigureFd);

        var batch = new[]
        {
            CanFrame.Classic(0x100, new byte[] { 0xA }),
            CanFrame.Fd(0x101, new byte[12], BRS: true),
            CanFrame.Classic(0x18FF50E5, new byte[] { 0xB }, isExtendedFrame: true),
        };
        tx.Transmit(batch).Should().Be(3);

        var received = await rx.ReceiveAsync(3, 2000, TestContext.Current.CancellationToken);
        received.Should().HaveCount(3);
        received[0].CanFrame.FrameKind.Should().Be(CanFrameType.Can20);
        received[1].CanFrame.FrameKind.Should().Be(CanFrameType.CanFd);
        received[2].CanFrame.FrameKind.Should().Be(CanFrameType.Can20);
        received[2].CanFrame.IsExtendedFrame.Should().BeTrue();
        received[2].CanFrame.ID.Should().Be(0x18FF50E5);
    }

    private static void ConfigureFd(IBusInitOptionsConfigurator cfg)
        => cfg.SetProtocolMode(CanProtocolMode.CanFd).Fd(500_000, 2_000_000).SetAsyncBufferCapacity(256);
}
#endif
