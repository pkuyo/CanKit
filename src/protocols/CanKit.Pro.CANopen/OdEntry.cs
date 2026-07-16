using System;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Data types supported by the MVP Object Dictionary (subset of CiA 301 Table 44). Each type
/// implies a fixed on-the-wire size (little-endian) used by SDO expedited encoding and PDO
/// mapping (FR-CO-001 / FR-CO-005).
/// </summary>
public enum OdDataType : byte
{
    /// <summary>1-byte unsigned integer (CiA 301 <c>UNSIGNED8</c>).</summary>
    Unsigned8 = 0x05,

    /// <summary>2-byte unsigned integer, little-endian (CiA 301 <c>UNSIGNED16</c>).</summary>
    Unsigned16 = 0x06,

    /// <summary>4-byte unsigned integer, little-endian (CiA 301 <c>UNSIGNED32</c>).</summary>
    Unsigned32 = 0x07,

    /// <summary>1-byte signed integer (CiA 301 <c>INTEGER8</c>).</summary>
    Integer8 = 0x02,

    /// <summary>2-byte signed integer, little-endian (CiA 301 <c>INTEGER16</c>).</summary>
    Integer16 = 0x03,

    /// <summary>4-byte signed integer, little-endian (CiA 301 <c>INTEGER32</c>).</summary>
    Integer32 = 0x04,

    /// <summary>Variable-length byte string (CiA 301 <c>DOMAIN</c>). Always exchanged via SDO
    /// segmented transfer regardless of length.</summary>
    Domain = 0x0F,
}

/// <summary>
/// Read/write access flag for an <see cref="OdEntry"/>. SDO write attempts against a read-only
/// entry produce SDO abort <c>0x06010002</c> ("attempt to write a read-only object").
/// </summary>
[Flags]
public enum OdAccess : byte
{
    /// <summary>Value is only readable.</summary>
    ReadOnly = 1,

    /// <summary>Value is only writable.</summary>
    WriteOnly = 2,

    /// <summary>Value is both readable and writable (default).</summary>
    ReadWrite = ReadOnly | WriteOnly,
}

/// <summary>
/// A single subindex entry in the local Object Dictionary (FR-CO-001): value + declared data
/// type + access flags. Stored inside <see cref="ObjectDictionary"/>; not intended to be
/// constructed directly by callers — use the fluent <c>Add*</c> methods on the dictionary.
/// </summary>
/// <remarks>
/// The stored raw value is a little-endian byte array in the same layout SDO expedited encoding
/// uses on the wire. This keeps the SDO server code path allocation-cheap (no round-trip through
/// typed converters on every request) while typed accessors on <see cref="ObjectDictionary"/>
/// preserve type-safety for local reads/writes.
/// </remarks>
public sealed class OdEntry
{
    private byte[] _value;

    internal OdEntry(OdDataType type, OdAccess access, byte[] value)
    {
        DataType = type;
        Access = access;
        _value = value;
    }

    /// <summary>CiA 301 data type of the entry.</summary>
    public OdDataType DataType { get; }

    /// <summary>Access permissions (read-only / write-only / read-write).</summary>
    public OdAccess Access { get; }

    /// <summary>Fixed on-the-wire size in bytes for the entry's <see cref="DataType"/>. For
    /// <see cref="OdDataType.Domain"/> the value is variable and this property returns the
    /// current stored length.</summary>
    public int Size => _value.Length;

    /// <summary>Snapshot of the raw little-endian bytes. Safe to hand out — callers get a
    /// fresh copy and cannot mutate the dictionary's private buffer.</summary>
    public byte[] GetRawValue()
    {
        var copy = new byte[_value.Length];
        Buffer.BlockCopy(_value, 0, copy, 0, _value.Length);
        return copy;
    }

    /// <summary>
    /// Replaces the stored raw bytes. Called by the SDO server on writes as well as by the
    /// dictionary's typed setters after they have serialized the caller's value. Enforces that
    /// fixed-width types keep their declared size; DOMAIN may grow/shrink.
    /// </summary>
    internal void SetRawValue(byte[] value)
    {
        if (DataType != OdDataType.Domain && value.Length != OdEntryLayout.FixedSize(DataType))
            throw new InvalidOperationException(
                $"OD entry expects {OdEntryLayout.FixedSize(DataType)} bytes for {DataType} but got {value.Length}.");
        _value = value;
    }

    /// <summary>Non-copying view of the current raw bytes, for the SDO server hot path.</summary>
    internal ReadOnlySpan<byte> RawSpan => _value;
}

internal static class OdEntryLayout
{
    internal static int FixedSize(OdDataType type) => type switch
    {
        OdDataType.Unsigned8 => 1,
        OdDataType.Integer8 => 1,
        OdDataType.Unsigned16 => 2,
        OdDataType.Integer16 => 2,
        OdDataType.Unsigned32 => 4,
        OdDataType.Integer32 => 4,
        OdDataType.Domain => -1,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown OD data type."),
    };
}
