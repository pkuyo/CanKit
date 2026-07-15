using System;
using CanKit.Pro.Addressing;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies the L2 addressing helpers (CanKit.Pro.Addressing, arc42 "Adressierungs-Helfer",
/// SRS FR-RAW-040): 11/29-bit CAN ID validation and J1939 PGN/Priority/PDU-Format/Source-Address
/// composition and decomposition.
/// </summary>
public class AddressingTests
{
    [Theory]
    [InlineData(0u, true)]
    [InlineData(0x7FFu, true)]
    [InlineData(0x800u, false)]
    [InlineData(0x1FFFFFFFu, false)]
    public void IsValidStandard_Matches_The_11_Bit_Boundary(uint id, bool expected)
    {
        CanIdRange.IsValidStandard(id).Should().Be(expected);
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(0x1FFFFFFFu, true)]
    [InlineData(0x20000000u, false)]
    public void IsValidExtended_Matches_The_29_Bit_Boundary(uint id, bool expected)
    {
        CanIdRange.IsValidExtended(id).Should().Be(expected);
    }

    [Fact]
    public void ValidateStandard_Throws_For_Out_Of_Range_And_Returns_The_Id_Otherwise()
    {
        CanIdRange.ValidateStandard(0x7FF).Should().Be(0x7FFu);
        Action act = () => CanIdRange.ValidateStandard(0x800);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ValidateExtended_Throws_For_Out_Of_Range_And_Returns_The_Id_Otherwise()
    {
        CanIdRange.ValidateExtended(0x1FFFFFFF).Should().Be(0x1FFFFFFFu);
        Action act = () => CanIdRange.ValidateExtended(0x20000000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // FR-RAW-040: PDU2 (broadcast-only, PF >= 240) round-trip -- the Group Extension (PS) is part
    // of the PGN, and there is no destination address.
    [Fact]
    public void J1939_Compose_Decompose_Round_Trips_For_A_Pdu2_Message()
    {
        var id = J1939Id.Compose(priority: 6, reserved: false, dataPage: 0, pduFormat: 0xFE, pduSpecific: 0xCA, sourceAddress: 0x2A);

        var fields = J1939Id.Decompose(id);

        fields.Priority.Should().Be(6);
        fields.Reserved.Should().BeFalse();
        fields.DataPage.Should().Be(0);
        fields.PduFormat.Should().Be(0xFE);
        fields.PduSpecific.Should().Be(0xCA);
        fields.SourceAddress.Should().Be(0x2A);
        fields.IsPdu1.Should().BeFalse();
        fields.DestinationAddress.Should().BeNull();
        fields.Pgn.Should().Be(0xFECAu);
    }

    // FR-RAW-040: PDU1 (peer-to-peer, destination-addressable, PF < 240) round-trip -- PS is a
    // destination address and is *not* part of the PGN.
    [Fact]
    public void J1939_Compose_Decompose_Round_Trips_For_A_Pdu1_Message()
    {
        var id = J1939Id.Compose(priority: 3, reserved: false, dataPage: 0, pduFormat: 0xC8, pduSpecific: 0x0B, sourceAddress: 0x17);

        var fields = J1939Id.Decompose(id);

        fields.PduFormat.Should().Be(0xC8);
        fields.IsPdu1.Should().BeTrue();
        fields.DestinationAddress.Should().Be(0x0B);
        fields.SourceAddress.Should().Be(0x17);
        fields.Pgn.Should().Be(0xC800u, "a PDU1 PGN excludes the destination-address byte (PS)");
    }

    [Fact]
    public void ComposePgn_Round_Trips_For_A_Pdu2_Pgn()
    {
        var id = J1939Id.ComposePgn(priority: 6, pgn: 0xFECA, sourceAddress: 0x2A);

        var fields = J1939Id.Decompose(id);

        fields.Pgn.Should().Be(0xFECAu);
        fields.SourceAddress.Should().Be(0x2A);
        fields.DestinationAddress.Should().BeNull();
    }

    [Fact]
    public void ComposePgn_Round_Trips_For_A_Pdu1_Pgn_With_A_Destination_Address()
    {
        var id = J1939Id.ComposePgn(priority: 3, pgn: 0xC800, sourceAddress: 0x17, destinationAddress: 0x0B);

        var fields = J1939Id.Decompose(id);

        fields.Pgn.Should().Be(0xC800u);
        fields.SourceAddress.Should().Be(0x17);
        fields.DestinationAddress.Should().Be(0x0B);
    }

    // Grenzwerte (SRS FR-RAW-040 verification criterion): the maximum possible 29-bit ID.
    [Fact]
    public void J1939_Decompose_Handles_The_Maximum_29_Bit_Id()
    {
        var fields = J1939Id.Decompose(0x1FFFFFFF);

        fields.Priority.Should().Be(7);
        fields.Reserved.Should().BeTrue();
        fields.DataPage.Should().Be(1);
        fields.PduFormat.Should().Be(0xFF);
        fields.PduSpecific.Should().Be(0xFF);
        fields.SourceAddress.Should().Be(0xFF);
    }

    [Fact]
    public void J1939_Decompose_Rejects_An_Id_That_Does_Not_Fit_In_29_Bits()
    {
        Action act = () => J1939Id.Decompose(0x20000000);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void J1939_Compose_Rejects_Priority_Above_Seven()
    {
        Action act = () => J1939Id.Compose(priority: 8, reserved: false, dataPage: 0, pduFormat: 0, pduSpecific: 0, sourceAddress: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
