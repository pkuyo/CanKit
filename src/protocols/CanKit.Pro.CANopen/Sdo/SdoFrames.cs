using System;

namespace CanKit.Pro.CANopen.Sdo;

/// <summary>
/// Encode/decode helpers for the SDO transfer protocols the MVP supports (CiA 301 §7.2.4):
/// expedited upload/download (payloads ≤ 4 bytes) and segmented upload/download (payloads > 4
/// bytes with the toggle bit). Block transfer is out of scope for the MVP.
/// </summary>
/// <remarks>
/// All frames are exactly eight bytes on the wire (per the SDO spec). Frames shorter than eight
/// bytes on the wire are still accepted on decode as long as they contain the fields the current
/// state machine needs — this matches what real ECUs sometimes emit under DLC padding rules.
/// </remarks>
internal static class SdoFrames
{
    // -------- Command specifier constants (CiA 301 §7.2.4) --------
    // Byte 0 layout for the various SDO commands.

    /// <summary>Initiate Domain Download (client → server), expedited, all 4 bytes valid.</summary>
    internal const byte CcsDownloadInitExpedited4 = 0x23;

    /// <summary>Initiate Domain Download (client → server), expedited, 1..3 bytes valid.</summary>
    internal const byte CcsDownloadInitExpeditedBase = 0x20; // OR-ed with ((4-n) << 2) | 0x03

    /// <summary>Initiate Domain Download (client → server), segmented (size indicated).</summary>
    internal const byte CcsDownloadInitSegmented = 0x21;

    /// <summary>Initiate Domain Upload (client → server).</summary>
    internal const byte CcsUploadInit = 0x40;

    /// <summary>Download segment (client → server), toggle bit will be OR-ed in.</summary>
    internal const byte CcsDownloadSegmentBase = 0x00;

    /// <summary>Upload segment (client → server), toggle bit will be OR-ed in.</summary>
    internal const byte CcsUploadSegmentBase = 0x60;

    /// <summary>Abort domain transfer (either direction).</summary>
    internal const byte CsAbort = 0x80;

    /// <summary>Initiate Domain Upload response (server → client), expedited, all 4 bytes valid.</summary>
    internal const byte ScsUploadInitExpedited4 = 0x43;

    /// <summary>Initiate Domain Upload response (server → client), expedited, 1..3 valid.</summary>
    internal const byte ScsUploadInitExpeditedBase = 0x40; // OR-ed with ((4-n) << 2) | 0x03

    /// <summary>Initiate Domain Upload response (server → client), segmented (size indicated).</summary>
    internal const byte ScsUploadInitSegmented = 0x41;

    /// <summary>Initiate Domain Download response (server → client).</summary>
    internal const byte ScsDownloadInitAck = 0x60;

    /// <summary>Download segment response (server → client), toggle bit will be OR-ed in.</summary>
    internal const byte ScsDownloadSegmentBase = 0x20;

    /// <summary>Upload segment response (server → client), toggle bit will be OR-ed in.</summary>
    internal const byte ScsUploadSegmentBase = 0x00;

    internal const byte ToggleBit = 0x10;
    internal const byte ContinueBit = 0x01; // 'c' bit — set means "no more segments" in a segment frame.

    /// <summary>Builds an SDO Abort frame (8 bytes).</summary>
    internal static byte[] BuildAbort(ushort index, byte subindex, uint abortCode)
    {
        var buf = new byte[8];
        buf[0] = CsAbort;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        buf[4] = (byte)(abortCode & 0xFF);
        buf[5] = (byte)((abortCode >> 8) & 0xFF);
        buf[6] = (byte)((abortCode >> 16) & 0xFF);
        buf[7] = (byte)((abortCode >> 24) & 0xFF);
        return buf;
    }

    /// <summary>Builds the client → server "Initiate Domain Upload" request (SDO read).</summary>
    internal static byte[] BuildUploadInit(ushort index, byte subindex)
    {
        var buf = new byte[8];
        buf[0] = CcsUploadInit;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        return buf;
    }

    /// <summary>Builds the client → server "Initiate Domain Download" request (SDO write). Uses
    /// expedited encoding when <paramref name="data"/> is 1..4 bytes, segmented (with a size
    /// indicator) otherwise. Segmented downloads always set the "size indicated" bit so the
    /// server knows the total payload length up front.</summary>
    internal static byte[] BuildDownloadInit(ushort index, byte subindex, ReadOnlySpan<byte> data)
    {
        var buf = new byte[8];
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;

        if (data.Length <= 4)
        {
            // Expedited: bits e=1, s=1, n = 4 - data.Length (bits 2..3).
            buf[0] = (byte)(CcsDownloadInitExpeditedBase | (((4 - data.Length) & 0x03) << 2) | 0x03);
            for (int i = 0; i < data.Length; i++) buf[4 + i] = data[i];
        }
        else
        {
            // Segmented: 0x21, followed by a little-endian 32-bit total length in bytes 4..7.
            buf[0] = CcsDownloadInitSegmented;
            uint len = (uint)data.Length;
            buf[4] = (byte)(len & 0xFF);
            buf[5] = (byte)((len >> 8) & 0xFF);
            buf[6] = (byte)((len >> 16) & 0xFF);
            buf[7] = (byte)((len >> 24) & 0xFF);
        }
        return buf;
    }

    /// <summary>Builds a segment frame (upload or download). Sets the toggle bit and the
    /// continue ('no more segments') bit according to <paramref name="toggle"/> and
    /// <paramref name="lastSegment"/>. Zero-fills unused data bytes; <paramref name="data"/>
    /// must not exceed 7 bytes.</summary>
    internal static byte[] BuildSegment(byte baseCs, bool toggle, bool lastSegment, ReadOnlySpan<byte> data)
    {
        if (data.Length > 7)
            throw new ArgumentException("SDO segment payload must fit in 7 bytes.", nameof(data));
        var buf = new byte[8];
        byte cs = baseCs;
        if (toggle) cs |= ToggleBit;
        int n = 7 - data.Length; // number of unused bytes in the 7-byte data window.
        cs |= (byte)((n & 0x07) << 1);
        if (lastSegment) cs |= ContinueBit;
        buf[0] = cs;
        for (int i = 0; i < data.Length; i++) buf[1 + i] = data[i];
        return buf;
    }

    /// <summary>Reads the (index, subindex) from an SDO initiate/abort frame.</summary>
    internal static (ushort Index, byte Subindex) ReadIndex(ReadOnlySpan<byte> frame)
    {
        ushort index = (ushort)(frame[1] | (frame[2] << 8));
        byte subindex = frame[3];
        return (index, subindex);
    }

    /// <summary>Extracts the 32-bit abort code from an SDO Abort frame.</summary>
    internal static uint ReadAbortCode(ReadOnlySpan<byte> frame)
        => (uint)(frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24));

    /// <summary>
    /// Extracts the expedited payload from a server upload-init response (0x43 / 0x47 / 0x4B /
    /// 0x4F) or from a client download-init request (0x23 / 0x27 / 0x2B / 0x2F). Returns 0..4
    /// bytes based on the encoded <c>n</c> field.
    /// </summary>
    internal static byte[] ReadExpeditedPayload(ReadOnlySpan<byte> frame)
    {
        byte cs = frame[0];
        bool sizeIndicated = (cs & 0x01) != 0;
        int n = (cs >> 2) & 0x03;
        int len = sizeIndicated ? 4 - n : 4;
        var buf = new byte[len];
        for (int i = 0; i < len; i++) buf[i] = frame[4 + i];
        return buf;
    }

    /// <summary>Reads the 32-bit total length declared by a segmented upload-init response
    /// (0x41). Returns 0 when the size-indicator bit is not set (unbounded transfer).</summary>
    internal static uint ReadSegmentedTotalLength(ReadOnlySpan<byte> frame)
    {
        byte cs = frame[0];
        bool sizeIndicated = (cs & 0x01) != 0;
        if (!sizeIndicated) return 0;
        return (uint)(frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24));
    }

    /// <summary>
    /// Extracts the payload bytes from a segment frame's 7-byte data window using the encoded
    /// <c>n</c> field for the count of unused bytes. Also returns whether this was the last
    /// segment (the "no more segments" bit) and the toggle bit that came in.
    /// </summary>
    internal static (byte[] Data, bool LastSegment, bool Toggle) ReadSegment(ReadOnlySpan<byte> frame)
    {
        byte cs = frame[0];
        bool toggle = (cs & ToggleBit) != 0;
        bool last = (cs & ContinueBit) != 0;
        int n = (cs >> 1) & 0x07;
        int len = 7 - n;
        var buf = new byte[len];
        for (int i = 0; i < len; i++) buf[i] = frame[1 + i];
        return (buf, last, toggle);
    }
}
