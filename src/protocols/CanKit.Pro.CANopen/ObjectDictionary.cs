using System;
using System.Collections.Generic;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Thread-safe in-memory Object Dictionary keyed by <c>(index, subindex)</c> per CiA 301 §7.4.6
/// (FR-CO-001). Provides typed read/write accessors that enforce the declared
/// <see cref="OdDataType"/>; typed reads against an entry of the wrong data type throw
/// <see cref="InvalidOperationException"/>.
/// </summary>
/// <remarks>
/// <para>
/// The dictionary is the single source of truth shared by the local application, the SDO server
/// and the PDO mapping layer. All lookups take the internal lock briefly to avoid tearing when
/// the SDO server writes concurrently with an application read; individual entry values are
/// copied out under the lock so no reference leaks back to caller state.
/// </para>
/// <para>
/// The MVP fixes storage to a <see cref="Dictionary{TKey,TValue}"/> plus a single mutex. It is
/// intended for tens of entries typical of an MVP node; if profiles grow into the thousands the
/// storage can be swapped without changing the public surface.
/// </para>
/// </remarks>
public sealed class ObjectDictionary
{
    private readonly Dictionary<uint, OdEntry> _entries = new();
    private readonly object _sync = new();

    /// <summary>Total number of registered <c>(index, subindex)</c> entries.</summary>
    public int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    /// <summary>Adds or replaces an <see cref="OdDataType.Unsigned8"/> entry.</summary>
    public OdEntry AddU8(ushort index, byte subindex, byte value, OdAccess access = OdAccess.ReadWrite)
        => Add(index, subindex, OdDataType.Unsigned8, access, new[] { value });

    /// <summary>Adds or replaces an <see cref="OdDataType.Unsigned16"/> entry.</summary>
    public OdEntry AddU16(ushort index, byte subindex, ushort value, OdAccess access = OdAccess.ReadWrite)
        => Add(index, subindex, OdDataType.Unsigned16, access,
            new[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) });

    /// <summary>Adds or replaces an <see cref="OdDataType.Unsigned32"/> entry.</summary>
    public OdEntry AddU32(ushort index, byte subindex, uint value, OdAccess access = OdAccess.ReadWrite)
        => Add(index, subindex, OdDataType.Unsigned32, access, EncodeU32(value));

    /// <summary>Adds or replaces an <see cref="OdDataType.Integer8"/> entry.</summary>
    public OdEntry AddI8(ushort index, byte subindex, sbyte value, OdAccess access = OdAccess.ReadWrite)
        => Add(index, subindex, OdDataType.Integer8, access, new[] { unchecked((byte)value) });

    /// <summary>Adds or replaces an <see cref="OdDataType.Integer16"/> entry.</summary>
    public OdEntry AddI16(ushort index, byte subindex, short value, OdAccess access = OdAccess.ReadWrite)
    {
        var u = unchecked((ushort)value);
        return Add(index, subindex, OdDataType.Integer16, access,
            new[] { (byte)(u & 0xFF), (byte)((u >> 8) & 0xFF) });
    }

    /// <summary>Adds or replaces an <see cref="OdDataType.Integer32"/> entry.</summary>
    public OdEntry AddI32(ushort index, byte subindex, int value, OdAccess access = OdAccess.ReadWrite)
        => Add(index, subindex, OdDataType.Integer32, access, EncodeU32(unchecked((uint)value)));

    /// <summary>Adds or replaces an <see cref="OdDataType.Domain"/> entry with the given initial
    /// bytes (may be empty; will be resized on later writes).</summary>
    public OdEntry AddDomain(ushort index, byte subindex, byte[] value, OdAccess access = OdAccess.ReadWrite)
    {
        var copy = new byte[value.Length];
        Buffer.BlockCopy(value, 0, copy, 0, value.Length);
        return Add(index, subindex, OdDataType.Domain, access, copy);
    }

    /// <summary>Attempts to look up the entry for <paramref name="index"/>/<paramref name="subindex"/>.</summary>
    public bool TryGet(ushort index, byte subindex, out OdEntry entry)
    {
        lock (_sync)
        {
            return _entries.TryGetValue(Key(index, subindex), out entry!);
        }
    }

    /// <summary>Reads an entry's raw little-endian byte value.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the entry does not exist.</exception>
    public byte[] ReadRaw(ushort index, byte subindex)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(Key(index, subindex), out var entry))
                throw new KeyNotFoundException($"OD entry 0x{index:X4}:{subindex:X2} not found.");
            return entry.GetRawValue();
        }
    }

    /// <summary>
    /// Attempts to atomically snapshot the raw little-endian bytes of an entry under the OD's
    /// internal lock. Preferred over <see cref="TryGet"/> + <see cref="OdEntry.GetRawValue"/>
    /// for the SDO server and TPDO hot paths: a concurrent <see cref="WriteRaw"/> from another
    /// thread cannot tear a snapshot taken under the lock, so readers see either the entire
    /// pre-write value or the entire post-write value — never a mix. Returns
    /// <see langword="false"/> when the entry does not exist (mirrors <see cref="TryGet"/>).
    /// </summary>
    public bool TryReadRaw(ushort index, byte subindex, out byte[] value)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(Key(index, subindex), out var entry))
            {
                value = Array.Empty<byte>();
                return false;
            }
            value = entry.GetRawValue();
            return true;
        }
    }

    /// <summary>Writes raw bytes to an entry; used both by application code and by the SDO
    /// server. Enforces size-consistency for fixed-width data types.</summary>
    /// <exception cref="KeyNotFoundException">Thrown when the entry does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="value"/>'s length
    /// does not match the entry's declared size.</exception>
    public void WriteRaw(ushort index, byte subindex, byte[] value)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(Key(index, subindex), out var entry))
                throw new KeyNotFoundException($"OD entry 0x{index:X4}:{subindex:X2} not found.");
            var copy = new byte[value.Length];
            Buffer.BlockCopy(value, 0, copy, 0, value.Length);
            entry.SetRawValue(copy);
        }
    }

    /// <summary>Reads a fixed-width unsigned entry as <see cref="uint"/> (upcasts U8/U16/U32).
    /// Throws when the declared type is not one of those three.</summary>
    public uint ReadUnsigned(ushort index, byte subindex)
    {
        lock (_sync)
        {
            var entry = Require(index, subindex);
            return entry.DataType switch
            {
                OdDataType.Unsigned8 => entry.RawSpan[0],
                OdDataType.Unsigned16 => (uint)(entry.RawSpan[0] | (entry.RawSpan[1] << 8)),
                OdDataType.Unsigned32 => DecodeU32(entry.RawSpan),
                _ => throw new InvalidOperationException(
                    $"OD entry 0x{index:X4}:{subindex:X2} is {entry.DataType}, not an unsigned type."),
            };
        }
    }

    /// <summary>Reads a fixed-width signed entry as <see cref="int"/> (upcasts I8/I16/I32).
    /// Throws when the declared type is not one of those three.</summary>
    public int ReadSigned(ushort index, byte subindex)
    {
        lock (_sync)
        {
            var entry = Require(index, subindex);
            return entry.DataType switch
            {
                OdDataType.Integer8 => unchecked((sbyte)entry.RawSpan[0]),
                OdDataType.Integer16 => unchecked((short)(entry.RawSpan[0] | (entry.RawSpan[1] << 8))),
                OdDataType.Integer32 => unchecked((int)DecodeU32(entry.RawSpan)),
                _ => throw new InvalidOperationException(
                    $"OD entry 0x{index:X4}:{subindex:X2} is {entry.DataType}, not a signed type."),
            };
        }
    }

    /// <summary>
    /// Writes a fixed-width unsigned value into an entry, enforcing the declared type width.
    /// Throws when the target entry is not U8/U16/U32.
    /// </summary>
    public void WriteUnsigned(ushort index, byte subindex, uint value)
    {
        lock (_sync)
        {
            var entry = Require(index, subindex);
            switch (entry.DataType)
            {
                case OdDataType.Unsigned8:
                    if (value > byte.MaxValue) throw ValueOutOfRange(index, subindex, entry.DataType);
                    entry.SetRawValue(new[] { (byte)value });
                    break;
                case OdDataType.Unsigned16:
                    if (value > ushort.MaxValue) throw ValueOutOfRange(index, subindex, entry.DataType);
                    entry.SetRawValue(new[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) });
                    break;
                case OdDataType.Unsigned32:
                    entry.SetRawValue(EncodeU32(value));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"OD entry 0x{index:X4}:{subindex:X2} is {entry.DataType}, not an unsigned type.");
            }
        }
    }

    private OdEntry Add(ushort index, byte subindex, OdDataType type, OdAccess access, byte[] value)
    {
        var entry = new OdEntry(type, access, value);
        lock (_sync)
        {
            _entries[Key(index, subindex)] = entry;
        }
        return entry;
    }

    private OdEntry Require(ushort index, byte subindex)
    {
        if (!_entries.TryGetValue(Key(index, subindex), out var entry))
            throw new KeyNotFoundException($"OD entry 0x{index:X4}:{subindex:X2} not found.");
        return entry;
    }

    private static InvalidOperationException ValueOutOfRange(ushort index, byte subindex, OdDataType type)
        => new($"Value out of range for OD entry 0x{index:X4}:{subindex:X2} ({type}).");

    private static uint Key(ushort index, byte subindex) => ((uint)index << 8) | subindex;

    private static byte[] EncodeU32(uint value) => new[]
    {
        (byte)(value & 0xFF),
        (byte)((value >> 8) & 0xFF),
        (byte)((value >> 16) & 0xFF),
        (byte)((value >> 24) & 0xFF),
    };

    private static uint DecodeU32(ReadOnlySpan<byte> data)
        => (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
}
