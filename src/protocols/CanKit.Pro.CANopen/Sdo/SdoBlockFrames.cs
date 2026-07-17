using System;

namespace CanKit.Pro.CANopen.Sdo;

/// <summary>
/// Encode/decode helpers for the CiA 301 §7.2.4.3.15 SDO block transfer protocol (FR-CO-004).
/// Both block download (client → server) and block upload (server → client) share the same
/// command-specifier layout, only the direction and the meaning of the "cc" / "sc" (client /
/// server CRC-supported) bits differ.
/// </summary>
/// <remarks>
/// The block protocol is command-oriented up to and including the initiate frames, then
/// stream-oriented for the segments in every sub-block, then command-oriented again for the
/// per-sub-block ACK and for the end-of-block frames. All segment frames are exactly eight
/// bytes: byte 0 is <c>(c &lt;&lt; 7) | seqno</c> (seqno in <c>1..blksize</c>, <c>c</c> = 1 means
/// "this is the last segment carrying data"), bytes 1..7 are up to 7 bytes of payload. The
/// unused bytes in the final segment are signalled through the "n" field of the end-of-block
/// frame (identical semantics to the segmented codec's n field).
/// </remarks>
internal static class SdoBlockFrames
{
    // -------- Client → server command specifiers --------
    // ccs = 6 for block download, ccs = 5 for block upload.

    /// <summary>Block download initiate (client → server), ccs=6, cs=0.
    /// Byte 0 layout: <c>110 0 0 cc s 0</c>. Combine with <c>(cc &lt;&lt; 2) | (s &lt;&lt; 1)</c>.
    /// Example: cc=1, s=1 → 0xC6.</summary>
    internal const byte CcsBlockDownloadInitBase = 0xC0;

    /// <summary>Block download end-of-block (client → server), ccs=6, cs=1.
    /// Byte 0 layout: <c>110 nnn 0 1</c>. Combine with <c>((n &amp; 0x07) &lt;&lt; 2)</c>.</summary>
    internal const byte CcsBlockDownloadEndBase = 0xC1;

    /// <summary>Block upload initiate (client → server), ccs=5, cs=0.
    /// Byte 0 layout: <c>101 0 0 cc 0 0</c>. Combine with <c>(cc &lt;&lt; 2)</c>.
    /// Example: cc=1 → 0xA4.</summary>
    internal const byte CcsBlockUploadInitBase = 0xA0;

    /// <summary>Block upload end response (client → server), ccs=5, cs=1 → 0xA1.</summary>
    internal const byte CcsBlockUploadEndResponse = 0xA1;

    /// <summary>Block upload sub-block ACK (client → server), ccs=5, cs=2 → 0xA2.
    /// Byte 1 = ackseq (last successfully received seqno), byte 2 = next blksize.</summary>
    internal const byte CcsBlockUploadSubBlockAck = 0xA2;

    /// <summary>Block upload "start" (client → server) that tells the server to begin sending
    /// segments after the initiate exchange, ccs=5, cs=3 → 0xA3.</summary>
    internal const byte CcsBlockUploadStart = 0xA3;

    // -------- Server → client command specifiers --------

    /// <summary>Block download initiate response (server → client), scs=5, ss=0.
    /// Byte 0 layout: <c>101 0 0 sc 0 0</c>. Combine with <c>(sc &lt;&lt; 2)</c>.
    /// Example: sc=1 → 0xA4. Byte 4 = the blksize the server is willing to accept.</summary>
    internal const byte ScsBlockDownloadInitResponseBase = 0xA0;

    /// <summary>Block download sub-block ACK (server → client), scs=5, ss=2 → 0xA2.
    /// Byte 1 = ackseq, byte 2 = next blksize.</summary>
    internal const byte ScsBlockDownloadSubBlockAck = 0xA2;

    /// <summary>Block download end response (server → client), scs=5, ss=1 → 0xA1.</summary>
    internal const byte ScsBlockDownloadEndResponse = 0xA1;

    /// <summary>Block upload initiate response (server → client), scs=6, ss=0.
    /// Byte 0 layout: <c>110 0 0 sc s 0</c>. Combine with <c>(sc &lt;&lt; 2) | (s &lt;&lt; 1)</c>.
    /// Example: sc=1, s=1 → 0xC6. Bytes 4..7 = declared total length (when s=1).</summary>
    internal const byte ScsBlockUploadInitResponseBase = 0xC0;

    /// <summary>Block upload end (server → client), scs=6, ss=1.
    /// Byte 0 layout: <c>110 nnn 0 1</c>. Combine with <c>((n &amp; 0x07) &lt;&lt; 2)</c>.
    /// Bytes 1..2 = CRC (little-endian).</summary>
    internal const byte ScsBlockUploadEndBase = 0xC1;

    /// <summary>
    /// Builds the client → server block download initiate frame.
    /// </summary>
    /// <param name="index">OD index.</param>
    /// <param name="subindex">OD subindex.</param>
    /// <param name="clientCrcSupported">Sets the "cc" bit (client CRC support).</param>
    /// <param name="sizeIndicated">Sets the "s" bit and copies <paramref name="totalSize"/> into bytes 4..7.</param>
    /// <param name="totalSize">Total download payload length in bytes when <paramref name="sizeIndicated"/> is true.</param>
    internal static byte[] BuildBlockDownloadInit(ushort index, byte subindex,
        bool clientCrcSupported, bool sizeIndicated, uint totalSize)
    {
        var buf = new byte[8];
        byte cs = CcsBlockDownloadInitBase;
        if (clientCrcSupported) cs |= 0x04;
        if (sizeIndicated) cs |= 0x02;
        buf[0] = cs;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        if (sizeIndicated)
        {
            buf[4] = (byte)(totalSize & 0xFF);
            buf[5] = (byte)((totalSize >> 8) & 0xFF);
            buf[6] = (byte)((totalSize >> 16) & 0xFF);
            buf[7] = (byte)((totalSize >> 24) & 0xFF);
        }
        return buf;
    }

    /// <summary>Builds the server → client block download initiate response, advertising the
    /// negotiated <paramref name="blockSize"/>.</summary>
    internal static byte[] BuildBlockDownloadInitResponse(ushort index, byte subindex,
        bool serverCrcSupported, byte blockSize)
    {
        var buf = new byte[8];
        byte cs = ScsBlockDownloadInitResponseBase;
        if (serverCrcSupported) cs |= 0x04;
        buf[0] = cs;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        buf[4] = blockSize;
        return buf;
    }

    /// <summary>Builds a block segment frame carrying up to seven data bytes. Seqno must be in
    /// <c>1..127</c> and <paramref name="isLastSegment"/> sets the "c" bit (bit 7 of byte 0).</summary>
    internal static byte[] BuildSegment(byte seqno, bool isLastSegment, ReadOnlySpan<byte> data)
    {
        if (seqno is < 1 or > 127)
            throw new ArgumentOutOfRangeException(nameof(seqno), seqno, "Block-transfer seqno must be in [1, 127].");
        if (data.Length > 7)
            throw new ArgumentException("Block-transfer segment payload must fit in 7 bytes.", nameof(data));
        var buf = new byte[8];
        byte cs = seqno;
        if (isLastSegment) cs |= 0x80;
        buf[0] = cs;
        for (int i = 0; i < data.Length; i++) buf[1 + i] = data[i];
        return buf;
    }

    /// <summary>Builds a sub-block acknowledgement frame (server → client for download, client
    /// → server for upload). Byte 0 is the caller-provided command specifier (0xA2 for both
    /// directions), byte 1 is the last-received seqno, byte 2 is the next block size.</summary>
    internal static byte[] BuildSubBlockAck(byte commandSpecifier, byte lastAckedSeq, byte nextBlockSize)
    {
        var buf = new byte[8];
        buf[0] = commandSpecifier;
        buf[1] = lastAckedSeq;
        buf[2] = nextBlockSize;
        return buf;
    }

    /// <summary>Builds a block download or upload end frame. The "n" field indicates how many of
    /// the seven data bytes in the final segment are unused (0..7). Bytes 1..2 optionally carry
    /// a CRC-16/XMODEM value; callers that did not negotiate CRC support should pass 0.</summary>
    internal static byte[] BuildEnd(byte commandSpecifierBase, byte unusedBytesInLastSegment, ushort crc)
    {
        if (unusedBytesInLastSegment > 7)
            throw new ArgumentOutOfRangeException(nameof(unusedBytesInLastSegment),
                unusedBytesInLastSegment, "Unused-bytes count in the last segment must be in [0, 7].");
        var buf = new byte[8];
        buf[0] = (byte)(commandSpecifierBase | ((unusedBytesInLastSegment & 0x07) << 2));
        buf[1] = (byte)(crc & 0xFF);
        buf[2] = (byte)((crc >> 8) & 0xFF);
        return buf;
    }

    /// <summary>Builds the trivial end-response frame (0xA1 for download, 0xA1 for upload).</summary>
    internal static byte[] BuildEndResponse(byte commandSpecifier)
    {
        var buf = new byte[8];
        buf[0] = commandSpecifier;
        return buf;
    }

    /// <summary>Builds the client → server block upload initiate frame with the requested
    /// <paramref name="blockSize"/> (byte 4) and protocol-switch threshold <paramref name="pst"/>
    /// (byte 5). We do not currently exercise the pst → segmented fallback but include the field
    /// so that peers see a well-formed frame.</summary>
    internal static byte[] BuildBlockUploadInit(ushort index, byte subindex,
        bool clientCrcSupported, byte blockSize, byte pst)
    {
        var buf = new byte[8];
        byte cs = CcsBlockUploadInitBase;
        if (clientCrcSupported) cs |= 0x04;
        buf[0] = cs;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        buf[4] = blockSize;
        buf[5] = pst;
        return buf;
    }

    /// <summary>Builds the server → client block upload initiate response with declared total
    /// size in bytes 4..7 when <paramref name="sizeIndicated"/> is true.</summary>
    internal static byte[] BuildBlockUploadInitResponse(ushort index, byte subindex,
        bool serverCrcSupported, bool sizeIndicated, uint totalSize)
    {
        var buf = new byte[8];
        byte cs = ScsBlockUploadInitResponseBase;
        if (serverCrcSupported) cs |= 0x04;
        if (sizeIndicated) cs |= 0x02;
        buf[0] = cs;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        if (sizeIndicated)
        {
            buf[4] = (byte)(totalSize & 0xFF);
            buf[5] = (byte)((totalSize >> 8) & 0xFF);
            buf[6] = (byte)((totalSize >> 16) & 0xFF);
            buf[7] = (byte)((totalSize >> 24) & 0xFF);
        }
        return buf;
    }

    /// <summary>Reads the sub-block-ack fields <c>(ackseq, nextBlksize)</c>.</summary>
    internal static (byte AckSeq, byte NextBlockSize) ReadSubBlockAck(ReadOnlySpan<byte> frame)
        => (frame[1], frame[2]);

    /// <summary>Reads the "n" (unused-bytes-in-last-segment) field from an end-of-block frame.
    /// Byte 0 layout is <c>xxx nnn xx</c>; extract bits 4..2.</summary>
    internal static byte ReadEndUnusedBytes(byte cs) => (byte)((cs >> 2) & 0x07);

    /// <summary>Reads the CRC-16 carried in an end-of-block frame (bytes 1..2, little-endian).</summary>
    internal static ushort ReadEndCrc(ReadOnlySpan<byte> frame)
        => (ushort)(frame[1] | (frame[2] << 8));

    /// <summary>Reads the total size declared in a block-upload initiate response's bytes 4..7.
    /// Returns 0 when the "s" (size-indicated) bit is not set.</summary>
    internal static uint ReadUploadTotalSize(ReadOnlySpan<byte> frame)
    {
        if ((frame[0] & 0x02) == 0) return 0;
        return (uint)(frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24));
    }

    /// <summary>Reads the "cc" or "sc" (CRC-supported) bit on a block initiate / initiate
    /// response. Layout puts it at bit 2 for both directions.</summary>
    internal static bool ReadCrcSupportedBit(byte cs) => (cs & 0x04) != 0;

    /// <summary>Computes CRC-16/XMODEM (poly 0x1021, init 0x0000, no reflection, no xor-out)
    /// over <paramref name="data"/>. This is the algorithm CiA 301 §7.2.4.3.15 references for the
    /// block-transfer CRC.</summary>
    internal static ushort ComputeCrc16Xmodem(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0) crc = (ushort)((crc << 1) ^ 0x1021);
                else crc <<= 1;
            }
        }
        return crc;
    }
}
