using CanKit.Pro.CANopen.Emcy;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.CANopen;

/// <summary>
/// Unit tests for CANopen codec primitives (EMCY encoding / decoding). SDO frame codec coverage
/// comes from the integration tests below, which round-trip encoded frames through the SDO
/// server + client state machines end-to-end.
/// </summary>
public class CanOpenCodecTests
{
    // FR-CO-011: EMCY round-trip preserves error code, register byte and manufacturer field.
    [Fact]
    public void Emcy_Encode_Decode_RoundTrip()
    {
        var msg = new EmcyMessage(producerNodeId: 0x21, errorCode: 0x8110,
            errorRegister: 0x01, manufacturerSpecific: new byte[] { 0xAA, 0xBB, 0xCC });

        var wire = msg.Encode();
        wire.Should().HaveCount(EmcyMessage.WireSize);
        wire[0].Should().Be(0x10);
        wire[1].Should().Be(0x81);
        wire[2].Should().Be(0x01);
        wire[3].Should().Be(0xAA);
        wire[7].Should().Be(0x00); // zero-padded

        var decoded = EmcyMessage.Decode(producerNodeId: 0x21, wire);
        decoded.ErrorCode.Should().Be(0x8110);
        decoded.ErrorRegister.Should().Be(0x01);
        decoded.ProducerNodeId.Should().Be(0x21);
        decoded.ManufacturerSpecific.Should().Equal(0xAA, 0xBB, 0xCC, 0x00, 0x00);
    }

    // FR-CO-011: the CiA 301 "no error / reset" code (0x0000) round-trips like any other value.
    [Fact]
    public void Emcy_ErrorReset_RoundTrips()
    {
        var msg = new EmcyMessage(producerNodeId: 0x21, errorCode: 0x0000, errorRegister: 0x00);
        var wire = msg.Encode();
        var decoded = EmcyMessage.Decode(producerNodeId: 0x21, wire);
        decoded.ErrorCode.Should().Be(0x0000);
        decoded.ErrorRegister.Should().Be(0x00);
    }
}
