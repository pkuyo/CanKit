using System;
using System.Collections.Generic;

namespace CanKit.Pro.CANopen.Pdo;

/// <summary>
/// A single entry in a PDO mapping: reference to an OD <c>(index, subindex)</c> plus the number
/// of bits contributed to the assembled PDO payload. Bit lengths must be multiples of eight in
/// this MVP (byte-aligned mapping), which matches every profile the tests exercise and keeps the
/// packing/unpacking loop trivially correct.
/// </summary>
/// <remarks>
/// Bit lengths are the same units the CiA 301 Table 61 "PDO Mapping Parameter" record uses
/// (byte 0 of a 32-bit mapping entry: <c>index &lt;&lt; 16 | subindex &lt;&lt; 8 | bit-length</c>).
/// The MVP does not yet parse those OD records at run time — mappings are configured through
/// <see cref="CanOpenNode"/>'s typed API instead.
/// </remarks>
public readonly struct PdoMappingEntry : IEquatable<PdoMappingEntry>
{
    /// <summary>Constructs a new mapping entry.</summary>
    /// <param name="index">Target OD index.</param>
    /// <param name="subindex">Target OD subindex.</param>
    /// <param name="bitLength">Field size in bits; must be a positive multiple of eight and no
    /// larger than 64.</param>
    public PdoMappingEntry(ushort index, byte subindex, byte bitLength)
    {
        if (bitLength == 0 || bitLength > 64 || bitLength % 8 != 0)
            throw new ArgumentOutOfRangeException(nameof(bitLength), bitLength,
                "MVP mapping requires positive byte-aligned bit lengths (8/16/24/32/40/48/56/64).");
        Index = index;
        Subindex = subindex;
        BitLength = bitLength;
    }

    /// <summary>OD index this entry references.</summary>
    public ushort Index { get; }

    /// <summary>OD subindex this entry references.</summary>
    public byte Subindex { get; }

    /// <summary>Contribution size in bits (always a multiple of 8 in this MVP).</summary>
    public byte BitLength { get; }

    /// <summary>Contribution size in bytes.</summary>
    public int ByteLength => BitLength / 8;

    /// <inheritdoc />
    public bool Equals(PdoMappingEntry other)
        => Index == other.Index && Subindex == other.Subindex && BitLength == other.BitLength;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PdoMappingEntry other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((Index * 397) ^ (Subindex * 31) ^ BitLength);
}

/// <summary>
/// TPDO transmission trigger (CiA 301 §7.2.6.4 Table 47). The MVP supports the three modes
/// listed here; other synchronous / RTR-only modes are Should-level and can be added without
/// changing the public API.
/// </summary>
public enum TpdoTransmission : byte
{
    /// <summary>Asynchronous, event-driven — the application calls
    /// <c>ICanOpenNode.TriggerTpdoAsync</c> to emit.</summary>
    EventDriven = 0,

    /// <summary>Periodic — the node's timer fires the TPDO at the configured interval.</summary>
    EventTimer = 1,

    /// <summary>Synchronous — TPDO is emitted every incoming SYNC frame.</summary>
    Synchronous = 2,
}

/// <summary>
/// Runtime PDO mapping table. Independent of the CANopen OD (the caller decides how / whether
/// to reflect the mapping into OD 0x1600/0x1A00), keyed by PDO number (1..4).
/// </summary>
public sealed class PdoMapping
{
    private readonly List<PdoMappingEntry> _entries = new();

    /// <summary>Constructs an empty mapping.</summary>
    public PdoMapping() { }

    /// <summary>Constructs a mapping from a copy of <paramref name="entries"/>.</summary>
    public PdoMapping(IEnumerable<PdoMappingEntry> entries)
    {
        _entries.AddRange(entries);
        Validate();
    }

    /// <summary>Total assembled payload size in bytes across every mapping entry.</summary>
    public int TotalBytes
    {
        get
        {
            int total = 0;
            foreach (var entry in _entries) total += entry.ByteLength;
            return total;
        }
    }

    /// <summary>Snapshot of the mapping entries (defensive copy so callers cannot mutate the
    /// live table).</summary>
    public IReadOnlyList<PdoMappingEntry> Entries => _entries.ToArray();

    /// <summary>Appends a new entry to the mapping.</summary>
    public PdoMapping Add(PdoMappingEntry entry)
    {
        _entries.Add(entry);
        Validate();
        return this;
    }

    /// <summary>Convenience overload of <see cref="Add(PdoMappingEntry)"/>.</summary>
    public PdoMapping Add(ushort index, byte subindex, byte bitLength)
        => Add(new PdoMappingEntry(index, subindex, bitLength));

    /// <summary>Removes every mapped entry.</summary>
    public void Clear() => _entries.Clear();

    private void Validate()
    {
        if (TotalBytes > 8)
            throw new InvalidOperationException(
                $"PDO mapping total ({TotalBytes} bytes) exceeds the 8-byte classic CAN limit.");
    }
}
