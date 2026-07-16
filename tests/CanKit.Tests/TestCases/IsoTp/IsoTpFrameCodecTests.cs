using System;
using System.Linq;
using CanKit.Pro.IsoTp;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.IsoTp;

/// <summary>
/// Deterministic codec tests for <see cref="IsoTpFrameCodec"/>. Each test also lists the
/// SRS/review requirement it covers so that traceability from the "review defects" list to the
/// verifying test is one grep away:
/// <list type="bullet">
///   <item><description>FR-TP-003 — classic vs CAN-FD frame kind chosen by caller (no inversion)</description></item>
///   <item><description>FR-TP-004 — FC uses PCI nibble 0x3 and padding never overwrites BS/STmin</description></item>
///   <item><description>FR-TP-005 — First-Frame length &gt; 255 round-trips</description></item>
///   <item><description>FR-TP-006 — STmin encoding accepts 0 ms and 1 ms</description></item>
///   <item><description>FR-TP-007 / FR-RAW-052 — reserved STmin -&gt; 127 ms; bounds-checked PCI parse</description></item>
///   <item><description>FR-TP-008 — first CF sequence number is 1, wraps 0..15</description></item>
///   <item><description>FR-TP-015 — classic-CAN SF is always ≤ 8 bytes</description></item>
/// </list>
/// </summary>
public class IsoTpFrameCodecTests
{
    // ---------------------------------------------------------------------------------------------
    // Defect #1 / FR-TP-003: classic vs CAN-FD is caller-controlled and not inverted.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildSingleFrame_ClassicCan_Produces_Eight_Byte_Frame_Regardless_Of_Padding()
    {
        var ep = IsoTpEndpoint.Normal(txCanId: 0x123, rxCanId: 0x124);
        var payload = new byte[] { 0x11, 0x22, 0x33 };

        var unpadded = IsoTpFrameCodec.BuildSingleFrame(ep, payload, isCanFd: false, padding: false);
        var padded = IsoTpFrameCodec.BuildSingleFrame(ep, payload, isCanFd: false, padding: true);

        unpadded.Length.Should().Be(4);
        padded.Length.Should().Be(8); // classic CAN always pads to 8
        padded.Length.Should().BeLessOrEqualTo(IsoTpFrameCodec.ClassicCanMaxData); // FR-TP-015
    }

    [Fact]
    public void BuildSingleFrame_CanFd_Can_Grow_Beyond_Eight_Bytes()
    {
        var ep = IsoTpEndpoint.Normal(txCanId: 0x123, rxCanId: 0x124);
        var payload = Enumerable.Range(0, 30).Select(i => (byte)i).ToArray();

        var frame = IsoTpFrameCodec.BuildSingleFrame(ep, payload, isCanFd: true, padding: true);

        frame.Length.Should().BeGreaterThan(IsoTpFrameCodec.ClassicCanMaxData);
        frame.Length.Should().Be(32); // next valid CAN-FD DLC >= 2 (PCI) + 30 (data) = 32
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #2 / FR-TP-004: FC PCI nibble is 0x3 (FlowControl), not 0x1 (FirstFrame).
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(FlowStatus.ClearToSend, 0x30)]
    [InlineData(FlowStatus.Wait, 0x31)]
    [InlineData(FlowStatus.Overflow, 0x32)]
    public void BuildFlowControl_Uses_Pci_Nibble_Three(FlowStatus flowStatus, byte expectedPciByte)
    {
        var ep = IsoTpEndpoint.Normal(0x7E0, 0x7E8);

        var frame = IsoTpFrameCodec.BuildFlowControl(ep, flowStatus, blockSize: 0x08,
            stMinRaw: 0x0A, isCanFd: false, padding: false);

        frame[0].Should().Be(expectedPciByte);
        frame[1].Should().Be(0x08);
        frame[2].Should().Be(0x0A);
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #3 / FR-TP-004: FC padding does NOT overwrite BS/STmin.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildFlowControl_Padding_Does_Not_Overwrite_Bs_Or_Stmin()
    {
        var ep = IsoTpEndpoint.Normal(0x7E0, 0x7E8);

        var frame = IsoTpFrameCodec.BuildFlowControl(ep, FlowStatus.ClearToSend,
            blockSize: 0x2A, stMinRaw: 0x5A, isCanFd: false, padding: true, paddingByte: 0xCC);

        frame.Length.Should().Be(8);
        frame[0].Should().Be(0x30);
        frame[1].Should().Be(0x2A); // BS preserved
        frame[2].Should().Be(0x5A); // STmin preserved
        frame[3].Should().Be(0xCC); // padding starts AFTER STmin
        frame[7].Should().Be(0xCC);
    }

    [Fact]
    public void BuildFlowControl_CanFd_Pads_To_Next_Valid_Dlc_Step_Without_Overwriting_Bs_Stmin()
    {
        var ep = IsoTpEndpoint.Normal(0x7E0, 0x7E8);

        var frame = IsoTpFrameCodec.BuildFlowControl(ep, FlowStatus.ClearToSend,
            blockSize: 0xAB, stMinRaw: 0xCD, isCanFd: true, padding: true);

        // 3 payload bytes -> next CAN-FD DLC step is 8.
        frame.Length.Should().Be(8);
        frame[0].Should().Be(0x30);
        frame[1].Should().Be(0xAB);
        frame[2].Should().Be(0xCD);
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #4 / FR-TP-005: First-Frame length > 255 round-trips.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1000)]
    [InlineData(4095)]
    public void FirstFrame_ClassicCan_LargeLength_RoundTrips(int totalLength)
    {
        var ep = IsoTpEndpoint.Normal(0x100, 0x101);
        var chunk = new byte[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };

        var frame = IsoTpFrameCodec.BuildFirstFrame(ep, totalLength, chunk, isCanFd: false);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.FirstFrame);
        pci.Length.Should().Be(totalLength);
    }

    [Theory]
    [InlineData(4096u)]      // just above the 12-bit classic limit
    [InlineData(65_535u)]
    [InlineData(1_000_000u)]
    public void FirstFrame_CanFd_LongLengthEscape_RoundTrips(uint totalLength)
    {
        var ep = IsoTpEndpoint.Normal(0x100, 0x101);
        var chunk = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();

        var frame = IsoTpFrameCodec.BuildFirstFrame(ep, (int)totalLength, chunk, isCanFd: true);

        frame.Length.Should().Be(IsoTpFrameCodec.CanFdMaxData);
        // First-Frame escape PCI: 0x10, 0x00, then 32-bit big-endian length
        frame[0].Should().Be(0x10);
        frame[1].Should().Be(0x00);
        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.FirstFrame);
        pci.Length.Should().Be((int)totalLength);
    }

    [Fact]
    public void FirstFrame_ClassicCan_Rejects_Length_Above_4095()
    {
        var ep = IsoTpEndpoint.Normal(0x100, 0x101);
        var chunk = new byte[] { 1, 2, 3, 4, 5, 6 };

        Action act = () => IsoTpFrameCodec.BuildFirstFrame(ep, 4096, chunk, isCanFd: false);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------------------------
    // Review §1.1 point 4 (bit-precedence): a 256-byte length must NOT be truncated to 0.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TryParsePci_FirstFrame_UsesCorrectHighNibbleOperatorPrecedence()
    {
        // Hand-crafted FF PCI announcing 0x100 (256) bytes: PCI = 0x11 0x00, data = 0xAA..
        var frame = new byte[] { 0x11, 0x00, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.FirstFrame);
        pci.Length.Should().Be(0x100);
        pci.DataOffset.Should().Be(2);
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #5 / FR-TP-006: EncodeStMin accepts 0 ms and 1 ms.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EncodeStMin_Accepts_Zero_And_One_Millisecond()
    {
        IsoTpFrameCodec.EncodeStMin(TimeSpan.Zero).Should().Be(0x00);
        IsoTpFrameCodec.EncodeStMin(TimeSpan.FromMilliseconds(1)).Should().Be(0x01);
    }

    [Theory]
    [InlineData(0, 0x00)]
    [InlineData(1, 0x01)]
    [InlineData(10, 0x0A)]
    [InlineData(50, 0x32)]
    [InlineData(127, 0x7F)]
    public void EncodeStMin_Millisecond_Range_RoundTrips(int ms, byte expectedRaw)
    {
        byte raw = IsoTpFrameCodec.EncodeStMin(TimeSpan.FromMilliseconds(ms));

        raw.Should().Be(expectedRaw);
        IsoTpFrameCodec.DecodeStMin(raw).Should().Be(TimeSpan.FromMilliseconds(ms));
    }

    [Theory]
    [InlineData(100, 0xF1)]
    [InlineData(200, 0xF2)]
    [InlineData(500, 0xF5)]
    [InlineData(900, 0xF9)]
    public void EncodeStMin_Submillisecond_Range_RoundTrips(int microseconds, byte expectedRaw)
    {
        byte raw = IsoTpFrameCodec.EncodeStMin(TimeSpan.FromTicks(microseconds * 10));

        raw.Should().Be(expectedRaw);
        IsoTpFrameCodec.DecodeStMin(raw).Should().Be(TimeSpan.FromTicks(microseconds * 10));
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #6 / FR-TP-007 / FR-RAW-052: reserved STmin bands decode to 127 ms.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0x80)]
    [InlineData(0xA5)]
    [InlineData(0xF0)]
    [InlineData(0xFA)]
    [InlineData(0xFD)]
    [InlineData(0xFF)]
    public void DecodeStMin_Reserved_Values_Are_Treated_As_127_Milliseconds(byte reservedRaw)
    {
        IsoTpFrameCodec.DecodeStMin(reservedRaw).Should().Be(TimeSpan.FromMilliseconds(0x7F));
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #6 / FR-TP-007: TryParsePci is bounds-safe for short frames.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TryParsePci_On_Empty_Payload_Returns_False_Without_Exception()
    {
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);
        IsoTpFrameCodec.TryParsePci(ReadOnlySpan<byte>.Empty, ep, isCanFd: false, out var pci).Should().BeFalse();
        pci.Should().Be(default(Pci));
    }

    [Fact]
    public void TryParsePci_Truncated_FlowControl_Returns_False_Without_Exception()
    {
        // FC PCI 0x30 alone (missing BS/STmin) must not throw IndexOutOfRangeException.
        var frame = new byte[] { 0x30 };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_Truncated_FirstFrame_Returns_False_Without_Exception()
    {
        var frame = new byte[] { 0x11 }; // FF PCI byte only, no length low byte
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_Truncated_FirstFrame_Escape_Returns_False_Without_Exception()
    {
        // FF escape header 0x10 0x00 present but 32-bit length missing (CAN-FD frame).
        var frame = new byte[] { 0x10, 0x00, 0x00 };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_Reserved_Pci_Nibble_Returns_False()
    {
        // PCI nibble 0x4 is reserved.
        var frame = new byte[] { 0x40, 0, 0, 0, 0, 0, 0, 0 };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_Reserved_FlowControl_FlowStatus_Returns_False()
    {
        var frame = new byte[] { 0x33, 0x00, 0x00 }; // FS = 3 is reserved
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_Extended_Addressing_Skips_First_Byte()
    {
        // Extended addressing: byte 0 = extension byte, byte 1 = PCI.
        var frame = new byte[] { 0xF7, 0x03, 0x11, 0x22, 0x33, 0, 0, 0 };
        var ep = IsoTpEndpoint.Extended(0x1, 0x2, sourceAddress: 0xF7, targetAddress: 0xF7);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.SingleFrame);
        pci.Length.Should().Be(3);
        pci.DataOffset.Should().Be(2); // 1 (ext) + 1 (PCI)
    }

    // ---------------------------------------------------------------------------------------------
    // Defect #7 / FR-TP-008: CF sequence numbers start at 1 and wrap 0..15.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FirstConsecutiveSequenceNumber_Is_One()
    {
        IsoTpFrameCodec.FirstConsecutiveSequenceNumber.Should().Be((byte)1);
    }

    [Theory]
    [InlineData((byte)0, (byte)1)]
    [InlineData((byte)1, (byte)2)]
    [InlineData((byte)14, (byte)15)]
    [InlineData((byte)15, (byte)0)]
    [InlineData((byte)16, (byte)1)] // callers may pass out-of-range values; codec masks to nibble
    public void NextConsecutiveSequenceNumber_Wraps_Zero_To_Fifteen(byte current, byte expected)
    {
        IsoTpFrameCodec.NextConsecutiveSequenceNumber(current).Should().Be(expected);
    }

    [Fact]
    public void BuildConsecutiveFrame_Sets_Correct_Sequence_Number_And_PCI()
    {
        var ep = IsoTpEndpoint.Normal(0x100, 0x101);

        var frame1 = IsoTpFrameCodec.BuildConsecutiveFrame(ep,
            IsoTpFrameCodec.FirstConsecutiveSequenceNumber,
            new byte[] { 1, 2, 3, 4, 5, 6, 7 }, isCanFd: false, padding: false);
        var frame2 = IsoTpFrameCodec.BuildConsecutiveFrame(ep, sequenceNumber: 15,
            new byte[] { 9, 9, 9, 9, 9, 9, 9 }, isCanFd: false, padding: false);
        var frameWrap = IsoTpFrameCodec.BuildConsecutiveFrame(ep, sequenceNumber: 0,
            new byte[] { 0, 0, 0, 0, 0, 0, 0 }, isCanFd: false, padding: false);

        frame1[0].Should().Be(0x21); // CF nibble 0x2 | SN=1
        frame2[0].Should().Be(0x2F); // CF nibble 0x2 | SN=15
        frameWrap[0].Should().Be(0x20); // CF nibble 0x2 | SN=0
    }

    // ---------------------------------------------------------------------------------------------
    // FR-TP-015: Classic-CAN SF is always ≤ 8 bytes and rejects oversized payloads.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void BuildSingleFrame_ClassicCan_Rejects_Payload_That_Exceeds_Seven_Bytes()
    {
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);
        var tooBig = new byte[8];

        Action act = () => IsoTpFrameCodec.BuildSingleFrame(ep, tooBig, isCanFd: false, padding: true);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildSingleFrame_ClassicCan_Extended_Addressing_Rejects_Payload_Above_Six_Bytes()
    {
        var ep = IsoTpEndpoint.Extended(0x1, 0x2, sourceAddress: 0x10, targetAddress: 0x11);
        var tooBig = new byte[7];

        Action act = () => IsoTpFrameCodec.BuildSingleFrame(ep, tooBig, isCanFd: false, padding: true);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------------------------------------
    // Round-trip: BuildXxx -> TryParsePci returns consistent length/SN/BS/STmin fields.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SingleFrame_RoundTrip(bool isCanFd, bool padding)
    {
        var ep = IsoTpEndpoint.Normal(0x123, 0x124);
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var frame = IsoTpFrameCodec.BuildSingleFrame(ep, payload, isCanFd, padding);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.SingleFrame);
        pci.Length.Should().Be(payload.Length);
        frame.AsSpan(pci.DataOffset, pci.Length).ToArray().Should().Equal(payload);
    }

    [Fact]
    public void SingleFrame_CanFd_EscapeForm_RoundTrip()
    {
        var ep = IsoTpEndpoint.Normal(0x123, 0x124);
        var payload = Enumerable.Range(0, 40).Select(i => (byte)(i + 1)).ToArray();

        var frame = IsoTpFrameCodec.BuildSingleFrame(ep, payload, isCanFd: true, padding: true);

        // Escape form: PCI byte 0 must be 0x00, LEN in byte 1.
        frame[0].Should().Be(0x00);
        frame[1].Should().Be((byte)payload.Length);
        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.SingleFrame);
        pci.Length.Should().Be(payload.Length);
        frame.AsSpan(pci.DataOffset, pci.Length).ToArray().Should().Equal(payload);
    }

    [Fact]
    public void FlowControl_RoundTrip_Preserves_Bs_And_StMin()
    {
        var ep = IsoTpEndpoint.Normal(0x7E0, 0x7E8);

        var frame = IsoTpFrameCodec.BuildFlowControl(ep, FlowStatus.Wait, blockSize: 0x11,
            stMinRaw: IsoTpFrameCodec.EncodeStMin(TimeSpan.FromMilliseconds(10)),
            isCanFd: false, padding: true);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.FlowControl);
        pci.FlowStatus.Should().Be(FlowStatus.Wait);
        pci.BlockSize.Should().Be((byte)0x11);
        pci.StMin.Should().Be(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void ConsecutiveFrame_RoundTrip_Preserves_Sequence_Number_And_Data()
    {
        var ep = IsoTpEndpoint.Normal(0x123, 0x124);
        var chunk = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70 };

        var frame = IsoTpFrameCodec.BuildConsecutiveFrame(ep, sequenceNumber: 5, chunk,
            isCanFd: false, padding: false);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.ConsecutiveFrame);
        pci.SequenceNumber.Should().Be((byte)5);
        frame.AsSpan(pci.DataOffset).ToArray().Should().Equal(chunk);
    }

    // ---------------------------------------------------------------------------------------------
    // Bugbot 3594958440: zero-length Single Frame is rejected at build time.
    // A classic-CAN SF with SF_DL=0 is invalid per ISO 15765-2, and TryParsePci would (correctly)
    // treat any low nibble of 0 as either the CAN-FD escape header or a malformed classic frame —
    // so building such a frame at all would produce something no conformant peer could parse.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BuildSingleFrame_Empty_Payload_Is_Rejected(bool isCanFd, bool padding)
    {
        var ep = IsoTpEndpoint.Normal(0x123, 0x124);

        Action act = () => IsoTpFrameCodec.BuildSingleFrame(ep, ReadOnlySpan<byte>.Empty, isCanFd, padding);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("userData");
    }

    [Fact]
    public void BuildSingleFrame_Empty_Payload_Is_Rejected_With_Extended_Addressing()
    {
        var ep = IsoTpEndpoint.Extended(0x1, 0x2, sourceAddress: 0x10, targetAddress: 0x11);

        Action act = () => IsoTpFrameCodec.BuildSingleFrame(ep, ReadOnlySpan<byte>.Empty,
            isCanFd: false, padding: true);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("userData");
    }

    // ---------------------------------------------------------------------------------------------
    // Bugbot 3594958440 + 3594958445: TryParsePci must know the frame kind. The Single-Frame
    // escape header (PCI nibble 0x0 low-nibble 0x0) and the First-Frame escape header
    // (0x10 0x00 + 32-bit length) are only defined on CAN-FD; on classic CAN those bit-patterns
    // are invalid and must be rejected — never mis-parsed as escape headers.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TryParsePci_SingleFrame_ClassicCan_Zero_Nibble_Is_Rejected()
    {
        // Classic-CAN SF PCI with low nibble = 0 is not defined by ISO 15765-2 and MUST NOT be
        // parsed as the CAN-FD escape form (which does not exist on classic CAN).
        var frame = new byte[] { 0x00, 0x03, 0x11, 0x22, 0x33, 0, 0, 0 };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_SingleFrame_CanFd_Zero_Nibble_Still_Parses_Escape_Form()
    {
        // Same first byte, but on CAN-FD it is the escape header: LEN then user data.
        var frame = new byte[]
        {
            0x00, 0x03, 0x11, 0x22, 0x33,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.SingleFrame);
        pci.Length.Should().Be(3);
        pci.DataOffset.Should().Be(2);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    public void TryParsePci_SingleFrame_CanFd_ShortForm_SfDl_Above_Seven_Is_Rejected(int sfDl)
    {
        // CAN-FD short-form SF PCI encodes SF_DL in the low nibble only for 1..7; 8..15 must use
        // the 0x00/LEN escape (bugbot 3596033572). Provide enough payload bytes so rejection is
        // due to the PCI encoding, not a truncated-frame bounds check.
        var frame = new byte[1 + sfDl];
        frame[0] = (byte)sfDl; // PCI type nibble 0, SF_DL = sfDl
        for (int i = 0; i < sfDl; i++)
            frame[1 + i] = (byte)(0x10 + i);
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_SingleFrame_CanFd_ShortForm_SfDl_Seven_Still_Parses()
    {
        // Boundary of the short form: SF_DL=7 remains valid on CAN-FD without the escape header.
        var frame = new byte[] { 0x07, 1, 2, 3, 4, 5, 6, 7 };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.SingleFrame);
        pci.Length.Should().Be(7);
        pci.DataOffset.Should().Be(1);
    }

    [Fact]
    public void TryParsePci_SingleFrame_CanFd_EscapeForm_Length_Eight_Still_Parses()
    {
        // Lengths 8+ must use escape; confirm the legal encoding is still accepted.
        var payload = new byte[8];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(0xA0 + i);
        var frame = IsoTpFrameCodec.BuildSingleFrame(IsoTpEndpoint.Normal(0x1, 0x2), payload,
            isCanFd: true, padding: true);

        frame[0].Should().Be(0x00);
        frame[1].Should().Be(8);

        IsoTpFrameCodec.TryParsePci(frame, IsoTpEndpoint.Normal(0x1, 0x2), isCanFd: true, out var pci)
            .Should().BeTrue();
        pci.Type.Should().Be(PciType.SingleFrame);
        pci.Length.Should().Be(8);
        pci.DataOffset.Should().Be(2);
    }

    [Fact]
    public void TryParsePci_FirstFrame_ClassicCan_Escape_Header_Is_Rejected()
    {
        // 0x10 0x00 on classic CAN is neither a valid FF (FF_DL == 0 is not permitted) nor an
        // escape header (escape form is CAN-FD-only). Must not be confused with the CAN-FD
        // escape header (bugbot 3594958445).
        var frame = new byte[] { 0x10, 0x00, 0x00, 0x00, 0x00, 0x0A, 0xAA, 0xBB };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParsePci_FirstFrame_CanFd_Escape_Header_Still_Parses()
    {
        // Same bit-pattern on CAN-FD MUST parse as the 32-bit escape header.
        var frame = new byte[]
        {
            0x10, 0x00, 0x00, 0x00, 0x10, 0x00, // total length = 0x1000 = 4096
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
            11, 12, 13, 14, 15, 16, 17, 18, 19, 20,
            21, 22, 23, 24,
        };
        var ep = IsoTpEndpoint.Normal(0x1, 0x2);

        IsoTpFrameCodec.TryParsePci(frame, ep, isCanFd: true, out var pci).Should().BeTrue();
        pci.Type.Should().Be(PciType.FirstFrame);
        pci.Length.Should().Be(0x1000);
        pci.DataOffset.Should().Be(6);
    }
}
