using System;
using System.Collections.Generic;
using CanKit.Pro.J1939;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.J1939;

/// <summary>
/// Verifies the SPN catalog (FR-J1939-002 convenience layer): the built-in SAE J1939-71
/// definitions decode crafted payloads to the expected physical values, custom SPNs can be
/// registered, and unknown SPNs fail loudly.
/// </summary>
public class J1939SpnCatalogTests
{
    [Fact]
    public void Default_Catalog_Decodes_Eec1_EngineSpeed()
    {
        // EEC1 with SPN 190 (Engine Speed) = 2500 rpm at byte offset 3 (0.125 rpm/bit).
        var payload = new byte[8];
        var raw = (ushort)(2500.0 / 0.125);
        payload[3] = (byte)(raw & 0xFF);
        payload[4] = (byte)(raw >> 8);

        var rpm = J1939SpnCatalog.Default.Extract(payload, 190);

        rpm.Should().BeApproximately(2500.0, 0.01);
    }

    [Fact]
    public void Default_Catalog_Decodes_Torque_And_Pedal_And_VehicleSpeed()
    {
        // EEC1: SPN 513 Actual Engine Percent Torque = 40 % (raw 165 with -125 offset).
        var eec1 = new byte[8];
        eec1[2] = 165;
        J1939SpnCatalog.Default.Extract(eec1, 513).Should().BeApproximately(40.0, 0.01);

        // EEC2: SPN 91 Accelerator Pedal Position 1 = 50 % (raw 125 at 0.4 %/bit).
        var eec2 = new byte[8];
        eec2[1] = 125;
        J1939SpnCatalog.Default.Extract(eec2, 91).Should().BeApproximately(50.0, 0.01);

        // CCVS: SPN 84 Wheel-Based Vehicle Speed = 90 km/h (raw 90*256 LE at offset 1).
        var ccvs = new byte[8];
        var raw = (ushort)(90 * 256);
        ccvs[1] = (byte)(raw & 0xFF);
        ccvs[2] = (byte)(raw >> 8);
        J1939SpnCatalog.Default.Extract(ccvs, 84).Should().BeApproximately(90.0, 0.01);
    }

    [Fact]
    public void Register_CustomSpn_Then_Extract_By_Number()
    {
        var catalog = new J1939SpnCatalog();
        catalog.Register(new J1939SpnDefinition(
            Spn: 4200, Name: "Vendor Oil Pressure", Pgn: 0xFE00,
            ByteOffset: 0, StartBit: 4, BitLength: 8, Resolution: 0.5, Offset: 0.0, Unit: "bar"));

        // Raw field: 8 bits at startBit 4 of byte 0 => value bits are payload[0] >> 4.
        var payload = new byte[] { 0x50, 0x00 }; // raw = 5 => 2.5 bar
        catalog.Extract(payload, 4200).Should().BeApproximately(2.5, 0.001);
    }

    [Fact]
    public void Extract_UnknownSpn_Throws_KeyNotFound()
    {
        var act = () => J1939SpnCatalog.Default.Extract(new byte[8], 9999);
        act.Should().Throw<KeyNotFoundException>();
    }
}
