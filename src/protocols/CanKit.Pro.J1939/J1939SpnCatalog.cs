using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CanKit.Pro.J1939;

/// <summary>
/// Registry of <see cref="J1939SpnDefinition"/>s keyed by SPN number: decode received PGN
/// payloads to physical values by SPN number instead of hand-written offsets/scales
/// (FR-J1939-002 convenience layer). <see cref="Default"/> ships a small set of ubiquitous
/// SAE J1939-71 parameters (EEC1/EEC2/CCVS); applications register the SPNs of their own
/// profiles on top. Thread-safe for concurrent reads; registration is expected at setup
/// time but is safe to do concurrently with reads.
/// </summary>
public sealed class J1939SpnCatalog : IEnumerable<J1939SpnDefinition>
{
    private readonly Dictionary<int, J1939SpnDefinition> _bySpn = new();
    private readonly object _gate = new();

    /// <summary>Catalog pre-populated with the common SAE J1939-71 SPNs below.</summary>
    public static J1939SpnCatalog Default { get; } = CreateDefault();

    /// <summary>Registers (or replaces) a definition. Returns the catalog for chaining.</summary>
    public J1939SpnCatalog Register(J1939SpnDefinition definition)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        lock (_gate)
        {
            _bySpn[definition.Spn] = definition;
        }
        return this;
    }

    /// <summary>Looks up a definition by SPN number.</summary>
    public bool TryGet(int spn, out J1939SpnDefinition? definition)
    {
        lock (_gate)
        {
            return _bySpn.TryGetValue(spn, out definition);
        }
    }

    /// <summary>Decodes SPN <paramref name="spn"/> from <paramref name="payload"/> using the
    /// registered definition. Throws <see cref="KeyNotFoundException"/> for unknown SPNs and
    /// <see cref="ArgumentOutOfRangeException"/> when the field exceeds the payload.</summary>
    public double Extract(ReadOnlySpan<byte> payload, int spn)
    {
        if (!TryGet(spn, out var definition) || definition is null)
        {
            throw new KeyNotFoundException($"SPN {spn} is not registered in this catalog.");
        }
        return definition.Extract(payload);
    }

    /// <inheritdoc />
    public IEnumerator<J1939SpnDefinition> GetEnumerator()
    {
        lock (_gate)
        {
            return _bySpn.Values.ToArray().AsEnumerable().GetEnumerator();
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static J1939SpnCatalog CreateDefault()
    {
        var catalog = new J1939SpnCatalog();

        // EEC1 (PGN 61444 / 0xF004) — SAE J1939-71 §5.2.
        catalog.Register(new J1939SpnDefinition(
            Spn: 512, Name: "Driver's Demand Engine Percent Torque", Pgn: 61444,
            ByteOffset: 1, StartBit: 0, BitLength: 8, Resolution: 1.0, Offset: -125.0, Unit: "%"));
        catalog.Register(new J1939SpnDefinition(
            Spn: 513, Name: "Actual Engine Percent Torque", Pgn: 61444,
            ByteOffset: 2, StartBit: 0, BitLength: 8, Resolution: 1.0, Offset: -125.0, Unit: "%"));
        catalog.Register(new J1939SpnDefinition(
            Spn: 190, Name: "Engine Speed", Pgn: 61444,
            ByteOffset: 3, StartBit: 0, BitLength: 16, Resolution: 0.125, Offset: 0.0, Unit: "rpm"));

        // EEC2 (PGN 61443 / 0xF003).
        catalog.Register(new J1939SpnDefinition(
            Spn: 91, Name: "Accelerator Pedal Position 1", Pgn: 61443,
            ByteOffset: 1, StartBit: 0, BitLength: 8, Resolution: 0.4, Offset: 0.0, Unit: "%"));
        catalog.Register(new J1939SpnDefinition(
            Spn: 92, Name: "Engine Percent Load At Current Speed", Pgn: 61443,
            ByteOffset: 2, StartBit: 0, BitLength: 8, Resolution: 1.0, Offset: 0.0, Unit: "%"));

        // CCVS (PGN 65265 / 0xFEE9).
        catalog.Register(new J1939SpnDefinition(
            Spn: 84, Name: "Wheel-Based Vehicle Speed", Pgn: 65265,
            ByteOffset: 1, StartBit: 0, BitLength: 16, Resolution: 1.0 / 256.0, Offset: 0.0, Unit: "km/h"));

        return catalog;
    }
}
