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
using FluentAssertions;
using Xunit;
// Alias the CanKit.Pro.IsoTp namespace root to avoid clashing with this test namespace's
// trailing "IsoTp" segment (same reason as in IsoTpChannelIntegrationTests).
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.IsoTp;

/// <summary>
/// CAN-FD coverage for the ISO-TP actor runtime (FR-TP-001 / FR-TP-003). Before these
/// tests, every positive round-trip ran on classic CAN only: the FD-specific paths
/// (FD single-frame escape, long FF &gt; 4095 bytes, <see cref="CanFrameType.CanFd"/>
/// emission and FD DLC padding in the channel) were exercised by codec unit tests but
/// never end-to-end with actor + TX-confirm + flow control.
/// </summary>
public class IsoTpCanFdTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(30);

    private static string NewSession() => $"isotp-fd-{Guid.NewGuid():N}";

    private static ICanBus OpenClassic(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static ICanBus OpenCanFd(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.CanFd).Fd(TestCaseProvider.AbitRate, TestCaseProvider.DbitRate));

    private static IsoTpChannelOptions FastOptions(bool useCanFd)
        => new()
        {
            UseCanFd = useCanFd,
            UsePadding = true,
            LocalBlockSize = 0,
            LocalStMin = TimeSpan.Zero,
            NAs = TimeSpan.FromMilliseconds(500),
            NBs = TimeSpan.FromMilliseconds(500),
            NCr = TimeSpan.FromMilliseconds(500),
            WftMax = 10,
        };

    [Fact]
    public async Task SingleFrame_Fd_RoundTrips_12_Bytes_Via_Escape_Sequence()
    {
        // 12 bytes do not fit a classic SF (max 7) and require the FD SF escape encoding
        // (PCI 0x00 + length byte) — a pure FD code path.
        var session = NewSession();
        using var busA = OpenCanFd(session, 0);
        using var busB = OpenCanFd(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x7E0, 0x7E8), FastOptions(useCanFd: true));
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x7E8, 0x7E0), FastOptions(useCanFd: true));

        var pdu = Enumerable.Range(1, 12).Select(i => (byte)i).ToArray();
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recvTask;
        got.Should().Equal(pdu);
    }

    [Fact]
    public async Task MultiFrame_Fd_RoundTrips_200_Bytes()
    {
        var session = NewSession();
        using var busA = OpenCanFd(session, 0);
        using var busB = OpenCanFd(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x101, 0x102), FastOptions(useCanFd: true));
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x102, 0x101), FastOptions(useCanFd: true));

        var pdu = Enumerable.Range(0, 200).Select(i => (byte)(i & 0xFF)).ToArray();
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recvTask;
        got.Should().Equal(pdu);
    }

    [Fact]
    public async Task MultiFrame_Fd_RoundTrips_LongFirstFrame_Above_4095_Bytes()
    {
        // Payloads > 4095 bytes require the long-FF escape form (FF_DL = 0 + 32-bit length),
        // which only exists in the FD code path of the codec.
        var session = NewSession();
        using var busA = OpenCanFd(session, 0);
        using var busB = OpenCanFd(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x201, 0x202), FastOptions(useCanFd: true));
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x202, 0x201), FastOptions(useCanFd: true));

        var pdu = new byte[5000];
        for (var i = 0; i < pdu.Length; i++)
        {
            pdu[i] = (byte)(i * 31 & 0xFF);
        }
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(LongTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recvTask;
        got.Should().Equal(pdu);
    }

    [Fact]
    public async Task Channel_With_UseCanFd_Emits_Only_CanFd_Frames_For_Sf_Ff_Cf_And_Fc()
    {
        // FR-TP-003 acceptance: with UseCanFd = true every frame the channels put on the
        // wire — SF, FF, CF and the peer's FC — must be a CAN-FD frame (the historical
        // defect was an inverted canfd ? Classic : Fd selection).
        var session = NewSession();
        using var busA = OpenCanFd(session, 0);
        using var busB = OpenCanFd(session, 1);
        using var snifferBus = OpenCanFd(session, 2);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x7E0, 0x7E8), FastOptions(useCanFd: true));
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x7E8, 0x7E0), FastOptions(useCanFd: true));

        var kinds = new List<(uint id, byte pci, CanFrameType kind)>();
        snifferBus.FrameObserved += (_, view) =>
        {
            var id = view.CanFrame.ID;
            if (id != 0x7E0 && id != 0x7E8)
            {
                return;
            }
            var data = view.CanFrame.Data.Span;
            var pci = data.Length == 0 ? (byte)0 : data[0];
            lock (kinds)
            {
                kinds.Add(((uint)id, pci, view.CanFrame.FrameKind));
            }
        };

        // Multi-frame exchange (produces FF + FC + CFs) ...
        var pdu = Enumerable.Range(0, 100).Select(i => (byte)(i & 0xFF)).ToArray();
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(pdu);
        (await recvTask).Should().Equal(pdu);

        // ... plus a single-frame exchange (produces SF).
        var sf = new byte[] { 0x22, 0xF1, 0x89 };
        var recvSf = receiver.ReceiveAsync(new CancellationTokenSource(ShortTimeout).Token);
        await sender.SendAsync(sf);
        (await recvSf).Should().Equal(sf);

        await Task.Delay(100); // let the hub deliver the tail frames to the sniffer

        List<(uint id, byte pci, CanFrameType kind)> observed;
        lock (kinds)
        {
            observed = kinds.ToList();
        }

        observed.Should().Contain(k => k.id == 0x7E0 && (k.pci & 0xF0) == 0x00, "an SF must be on the wire");
        observed.Should().Contain(k => k.id == 0x7E0 && (k.pci & 0xF0) == 0x10, "an FF must be on the wire");
        observed.Should().Contain(k => k.id == 0x7E0 && (k.pci & 0xF0) == 0x20, "a CF must be on the wire");
        observed.Should().Contain(k => k.id == 0x7E8 && (k.pci & 0xF0) == 0x30, "an FC must be on the wire");
        observed.Should().OnlyContain(k => k.kind == CanFrameType.CanFd,
            "UseCanFd = true must emit exclusively CAN-FD frames (FR-TP-003)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(200)]
    [InlineData(4095)]
    public async Task ClassicCan_RoundTrips_Payload_Length_Sweep(int length)
    {
        // FR-TP-001 length sweep: the SRS asks for SF..multi-frame coverage of the full
        // 1..4095 byte range; before, only 3/20/200 bytes were round-tripped.
        var session = NewSession();
        using var busA = OpenClassic(session, 0);
        using var busB = OpenClassic(session, 1);

        using var sender = IsoTpFactory.Open(busA, IsoTpEndpoint.Normal(0x7E0, 0x7E8), FastOptions(useCanFd: false));
        using var receiver = IsoTpFactory.Open(busB, IsoTpEndpoint.Normal(0x7E8, 0x7E0), FastOptions(useCanFd: false));

        var pdu = new byte[length];
        for (var i = 0; i < length; i++)
        {
            pdu[i] = (byte)(i * 17 & 0xFF);
        }
        var recvTask = receiver.ReceiveAsync(new CancellationTokenSource(LongTimeout).Token);
        await sender.SendAsync(pdu);
        var got = await recvTask;
        got.Should().Equal(pdu);
    }
}
