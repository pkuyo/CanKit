using System;

namespace CanKit.Pro.J1939;

/// <summary>
/// Describes one Suspect Parameter Number (SPN) from a J1939 parameter group: where the
/// field sits in the PGN payload and how to scale the raw value to a physical one
/// (SAE J1939-71 §5.1.3). Used by <see cref="J1939SpnCatalog"/> to decode SPNs by number
/// instead of by hand-written offsets (FR-J1939-002 convenience layer).
/// </summary>
/// <param name="Spn">The SPN number (SAE J1939-71).</param>
/// <param name="Name">Human-readable parameter name.</param>
/// <param name="Pgn">PGN that carries the SPN (informational; the same SPN can in principle
/// appear in more than one PGN — decode works on any payload).</param>
/// <param name="ByteOffset">Zero-based byte index of the field's first byte in the payload.</param>
/// <param name="StartBit">Zero-based bit index within <paramref name="ByteOffset"/> (0 = LSB).</param>
/// <param name="BitLength">Field size in bits (1..64).</param>
/// <param name="Resolution">Physical units per raw increment (scale factor).</param>
/// <param name="Offset">Physical value at raw 0.</param>
/// <param name="Unit">Physical unit string (e.g. "rpm", "km/h", "%").</param>
public sealed record J1939SpnDefinition(
    int Spn,
    string Name,
    uint Pgn,
    int ByteOffset,
    int StartBit,
    int BitLength,
    double Resolution,
    double Offset,
    string Unit)
{
    /// <summary>Decodes this SPN from a received PGN payload:
    /// <c>physical = raw × Resolution + Offset</c>.</summary>
    public double Extract(ReadOnlySpan<byte> payload)
        => J1939Spn.Extract(payload, ByteOffset, StartBit, BitLength, Resolution, Offset);
}
