using System;

namespace CanKit.Pro.J1939;

/// <summary>
/// Static helpers for extracting SAE J1939-71 SPN (Suspect Parameter Number) values from a
/// PGN payload using configurable scale/offset (SRS FR-J1939-002).
/// </summary>
/// <remarks>
/// <para>
/// SAE J1939-71 encodes each SPN as an unsigned integer field of 1..64 bits at a fixed byte
/// (and bit) offset inside its PGN payload, with a linear transform to physical units:
/// <c>physical = raw * resolution + offset</c>. This helper implements exactly that.
/// </para>
/// <para>
/// Byte order is <b>little-endian</b>, matching SAE J1939-71 §5.1.3. Bit fields that start at
/// an offset within a byte are read across byte boundaries with the low bits coming from the
/// low-indexed byte, in line with the standard.
/// </para>
/// </remarks>
public static class J1939Spn
{
    /// <summary>
    /// Extracts an unsigned SPN raw value from <paramref name="payload"/> at
    /// <paramref name="byteOffset"/> starting at bit <paramref name="startBit"/> (0..7) and
    /// spanning <paramref name="bitLength"/> bits (1..64), little-endian.
    /// </summary>
    /// <param name="payload">The PGN payload bytes.</param>
    /// <param name="byteOffset">Zero-based byte position of the first bit.</param>
    /// <param name="startBit">Zero-based bit index within <paramref name="byteOffset"/> byte
    /// (0..7). 0 = least-significant bit of the byte, as in SAE J1939-71 §5.1.3.</param>
    /// <param name="bitLength">Number of bits (1..64).</param>
    /// <exception cref="ArgumentOutOfRangeException">Any argument is out of range or the
    /// requested field extends past <paramref name="payload"/>.</exception>
    public static ulong ExtractRaw(ReadOnlySpan<byte> payload, int byteOffset, int startBit, int bitLength)
    {
        if (byteOffset < 0) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        if ((uint)startBit > 7) throw new ArgumentOutOfRangeException(nameof(startBit));
        if (bitLength is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(bitLength));
        int totalBits = startBit + bitLength;
        int bytesNeeded = byteOffset + ((totalBits + 7) >> 3);
        if (bytesNeeded > payload.Length)
            throw new ArgumentOutOfRangeException(nameof(bitLength),
                $"SPN field extends past the payload (offset={byteOffset}, startBit={startBit}, bitLength={bitLength}, payload={payload.Length}).");

        ulong acc = 0;
        int remaining = bitLength;
        int bitPos = startBit;
        int currentByte = byteOffset;
        int outBit = 0;
        while (remaining > 0)
        {
            int take = Math.Min(8 - bitPos, remaining);
            int mask = (1 << take) - 1;
            ulong chunk = (ulong)((payload[currentByte] >> bitPos) & mask);
            acc |= chunk << outBit;
            outBit += take;
            remaining -= take;
            currentByte++;
            bitPos = 0;
        }
        return acc;
    }

    /// <summary>
    /// Extracts an SPN and applies the linear transform
    /// <c>physical = raw * <paramref name="resolution"/> + <paramref name="offset"/></c>
    /// (SRS FR-J1939-002, SAE J1939-71 §5.1.3).
    /// </summary>
    public static double Extract(ReadOnlySpan<byte> payload, int byteOffset, int startBit,
        int bitLength, double resolution, double offset)
    {
        ulong raw = ExtractRaw(payload, byteOffset, startBit, bitLength);
        return raw * resolution + offset;
    }

    /// <summary>
    /// Overload for a byte-aligned SPN (starts at bit 0 of <paramref name="byteOffset"/>).
    /// </summary>
    public static double Extract(ReadOnlySpan<byte> payload, int byteOffset, int bitLength,
        double resolution, double offset)
        => Extract(payload, byteOffset, startBit: 0, bitLength, resolution, offset);

    /// <summary>
    /// Encodes an SPN raw value back into a payload buffer, little-endian. Useful for tests and
    /// for round-tripping simulated PGN payloads.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The requested field extends past
    /// <paramref name="payload"/>.</exception>
    public static void WriteRaw(Span<byte> payload, int byteOffset, int startBit, int bitLength, ulong rawValue)
    {
        if (byteOffset < 0) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        if ((uint)startBit > 7) throw new ArgumentOutOfRangeException(nameof(startBit));
        if (bitLength is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(bitLength));
        int totalBits = startBit + bitLength;
        int bytesNeeded = byteOffset + ((totalBits + 7) >> 3);
        if (bytesNeeded > payload.Length)
            throw new ArgumentOutOfRangeException(nameof(bitLength),
                $"SPN field extends past the payload (offset={byteOffset}, startBit={startBit}, bitLength={bitLength}, payload={payload.Length}).");

        int remaining = bitLength;
        int bitPos = startBit;
        int currentByte = byteOffset;
        int inBit = 0;
        while (remaining > 0)
        {
            int take = Math.Min(8 - bitPos, remaining);
            int mask = (1 << take) - 1;
            byte chunk = (byte)((rawValue >> inBit) & (ulong)mask);
            payload[currentByte] = (byte)((payload[currentByte] & ~(mask << bitPos)) | (chunk << bitPos));
            inBit += take;
            remaining -= take;
            currentByte++;
            bitPos = 0;
        }
    }
}
