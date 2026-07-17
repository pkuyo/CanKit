using System;
using System.Collections.Generic;
using CanKit.Pro.CANopen;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases.CANopen;

/// <summary>
/// Unit tests for the runtime local Object Dictionary (SRS FR-CO-001). No bus, no async — the
/// dictionary is a pure in-process data structure so it can be exercised in isolation before
/// the SDO server / PDO layer wire it into the node loop.
/// </summary>
public class ObjectDictionaryTests
{
    // FR-CO-001: round-trip typed writes read back with the correct value.
    [Fact]
    public void U32_RoundTrip_ReturnsWrittenValue()
    {
        var od = new ObjectDictionary();
        od.AddU32(0x2000, 0x00, 0xDEADBEEFu);

        od.ReadUnsigned(0x2000, 0x00).Should().Be(0xDEADBEEFu);
        od.ReadRaw(0x2000, 0x00).Should().Equal(0xEF, 0xBE, 0xAD, 0xDE);
    }

    // FR-CO-001: typed writes into a fixed-width entry must respect the declared width.
    [Fact]
    public void U16_TypedWrite_RejectsOverflow()
    {
        var od = new ObjectDictionary();
        od.AddU16(0x2001, 0x00, 0);
        Assert.Throws<InvalidOperationException>(() =>
            od.WriteUnsigned(0x2001, 0x00, 0x1_0000u));
    }

    // FR-CO-001: typed read against the wrong data-type family raises rather than silently
    // returning garbage.
    [Fact]
    public void SignedRead_ThrowsOnUnsignedEntry()
    {
        var od = new ObjectDictionary();
        od.AddU32(0x2002, 0x00, 42);
        Assert.Throws<InvalidOperationException>(() => od.ReadSigned(0x2002, 0x00));
    }

    // FR-CO-001: missing entries are visible via TryGet without raising.
    [Fact]
    public void TryGet_MissingEntry_ReturnsFalse()
    {
        var od = new ObjectDictionary();
        od.TryGet(0x1234, 0x00, out _).Should().BeFalse();
        Assert.Throws<KeyNotFoundException>(() => od.ReadRaw(0x1234, 0x00));
    }

    // FR-CO-001: DOMAIN entries can grow/shrink on each raw write.
    [Fact]
    public void Domain_Roundtrip_AllowsResize()
    {
        var od = new ObjectDictionary();
        od.AddDomain(0x2100, 0x00, new byte[] { 1, 2, 3 });
        od.ReadRaw(0x2100, 0x00).Should().Equal(1, 2, 3);

        od.WriteRaw(0x2100, 0x00, new byte[] { 9, 8, 7, 6, 5 });
        od.ReadRaw(0x2100, 0x00).Should().Equal(9, 8, 7, 6, 5);
    }

    // FR-CO-001: read-only / write-only access flags survive round-trip and are visible to
    // higher-level code (they are what the SDO server uses to emit the right abort code).
    [Fact]
    public void AccessFlags_Preserved()
    {
        var od = new ObjectDictionary();
        od.AddU16(0x1000, 0x00, 0x1234, OdAccess.ReadOnly);
        od.AddU16(0x1000, 0x01, 0x5678, OdAccess.WriteOnly);
        od.TryGet(0x1000, 0x00, out var ro).Should().BeTrue();
        ro.Access.Should().Be(OdAccess.ReadOnly);
        od.TryGet(0x1000, 0x01, out var wo).Should().BeTrue();
        wo.Access.Should().Be(OdAccess.WriteOnly);
    }
}
