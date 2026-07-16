using System;
using CanKit.Pro.Addressing;

namespace CanKit.Pro.J1939Tp;

/// <summary>
/// SAE J1939-21 §5.10 Transport Protocol wire-format codec: builds and parses the two protocol
/// PGNs used by every session -- TP.CM (0xEC00) and TP.DT (0xEB00) -- into their plain 8-byte
/// payload representations. Pure functions; no allocation of any state, no timing knowledge.
/// </summary>
/// <remarks>
/// All multi-byte length fields on the wire are little-endian (J1939-21 §5.10.3) and every
/// unused byte is padded to <c>0xFF</c>, matching the reference-stack convention on the bus.
/// </remarks>
internal static class J1939TpFrames
{
    /// <summary>Broadcast destination address (0xFF) used by BAM and by any PGN sent globally.</summary>
    public const byte GlobalDestinationAddress = J1939Pgn.GlobalAddress;

    /// <summary>Number of user-data bytes carried by one TP.DT frame (J1939-21 §5.10.4).</summary>
    public const int DtDataBytes = 7;

    /// <summary>Minimum payload length that must be transported via TP (J1939-21 §5.10.1: &gt;8 bytes).</summary>
    public const int MinTpPayloadLength = 9;

    /// <summary>
    /// Maximum payload length carried by any J1939-TP session -- 255 &#215; 7 = 1785 bytes
    /// (J1939-21 §5.10.1 both for BAM and CM), because the sequence number is a single byte
    /// starting at 1.
    /// </summary>
    public const int MaxTpPayloadLength = 255 * DtDataBytes;

    /// <summary>Default padding byte used for unused positions in the 8-byte TP.CM frames.</summary>
    public const byte PaddingByte = 0xFF;

    /// <summary>TP.CM control byte: Request To Send (J1939-21 §5.10.3).</summary>
    public const byte ControlRts = 0x10;

    /// <summary>TP.CM control byte: Clear To Send.</summary>
    public const byte ControlCts = 0x11;

    /// <summary>TP.CM control byte: End Of Message Acknowledgement.</summary>
    public const byte ControlEomAck = 0x13;

    /// <summary>TP.CM control byte: Broadcast Announce Message.</summary>
    public const byte ControlBam = 0x20;

    /// <summary>TP.CM control byte: Connection Abort.</summary>
    public const byte ControlAbort = 0xFF;

    /// <summary>
    /// Number of 7-byte data segments required to carry <paramref name="payloadLength"/> bytes.
    /// </summary>
    public static int TotalPackets(int payloadLength)
    {
        if (payloadLength <= 0) throw new ArgumentOutOfRangeException(nameof(payloadLength));
        return (payloadLength + DtDataBytes - 1) / DtDataBytes;
    }

    /// <summary>
    /// Builds an 8-byte TP.CM Broadcast Announce Message: control 0x20, total message size (u16
    /// LE), total packets, reserved 0xFF, PGN of the announced multi-packet message (24 bits LE).
    /// </summary>
    public static byte[] BuildBam(int totalBytes, int totalPackets, uint dataPgn)
    {
        ValidateTotalBytes(totalBytes);
        ValidateTotalPackets(totalPackets);
        ValidateDataPgn(dataPgn);

        var payload = new byte[8];
        payload[0] = ControlBam;
        payload[1] = (byte)(totalBytes & 0xFF);
        payload[2] = (byte)((totalBytes >> 8) & 0xFF);
        payload[3] = (byte)totalPackets;
        payload[4] = 0xFF; // reserved
        WriteDataPgn(payload, dataPgn);
        return payload;
    }

    /// <summary>Builds an 8-byte TP.CM Request To Send.</summary>
    public static byte[] BuildRts(int totalBytes, int totalPackets, byte maxPacketsPerCts, uint dataPgn)
    {
        ValidateTotalBytes(totalBytes);
        ValidateTotalPackets(totalPackets);
        ValidateDataPgn(dataPgn);

        var payload = new byte[8];
        payload[0] = ControlRts;
        payload[1] = (byte)(totalBytes & 0xFF);
        payload[2] = (byte)((totalBytes >> 8) & 0xFF);
        payload[3] = (byte)totalPackets;
        payload[4] = maxPacketsPerCts; // 0xFF = "no limit"; we always advertise a concrete cap.
        WriteDataPgn(payload, dataPgn);
        return payload;
    }

    /// <summary>Builds an 8-byte TP.CM Clear To Send.</summary>
    public static byte[] BuildCts(byte numPackets, byte nextPacketSn, uint dataPgn)
    {
        ValidateDataPgn(dataPgn);
        if (numPackets == 0) throw new ArgumentOutOfRangeException(nameof(numPackets), "CTS numPackets must be > 0.");
        if (nextPacketSn == 0) throw new ArgumentOutOfRangeException(nameof(nextPacketSn), "SN must be >= 1.");

        var payload = new byte[8];
        payload[0] = ControlCts;
        payload[1] = numPackets;
        payload[2] = nextPacketSn;
        payload[3] = 0xFF; // reserved
        payload[4] = 0xFF; // reserved
        WriteDataPgn(payload, dataPgn);
        return payload;
    }

    /// <summary>Builds an 8-byte TP.CM End Of Message Acknowledgement.</summary>
    public static byte[] BuildEomAck(int totalBytes, int totalPackets, uint dataPgn)
    {
        ValidateTotalBytes(totalBytes);
        ValidateTotalPackets(totalPackets);
        ValidateDataPgn(dataPgn);

        var payload = new byte[8];
        payload[0] = ControlEomAck;
        payload[1] = (byte)(totalBytes & 0xFF);
        payload[2] = (byte)((totalBytes >> 8) & 0xFF);
        payload[3] = (byte)totalPackets;
        payload[4] = 0xFF; // reserved
        WriteDataPgn(payload, dataPgn);
        return payload;
    }

    /// <summary>Builds an 8-byte TP.CM Connection Abort with reason <paramref name="reason"/>.</summary>
    public static byte[] BuildAbort(J1939TpAbortReason reason, uint dataPgn)
    {
        ValidateDataPgn(dataPgn);
        var payload = new byte[8];
        payload[0] = ControlAbort;
        payload[1] = (byte)reason;
        payload[2] = 0xFF;
        payload[3] = 0xFF;
        payload[4] = 0xFF;
        WriteDataPgn(payload, dataPgn);
        return payload;
    }

    /// <summary>
    /// Builds one 8-byte TP.DT frame carrying up to 7 bytes of user data at zero-based
    /// <paramref name="offset"/> within the full PDU, tagged with sequence number
    /// <paramref name="sn"/> (1..255). Missing bytes in the last packet are padded with 0xFF.
    /// </summary>
    public static byte[] BuildDt(byte sn, ReadOnlySpan<byte> pdu, int offset)
    {
        if (sn == 0) throw new ArgumentOutOfRangeException(nameof(sn), "TP.DT SN must be >= 1.");
        if ((uint)offset >= (uint)pdu.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var payload = new byte[8];
        payload[0] = sn;
        int remaining = pdu.Length - offset;
        int copy = Math.Min(DtDataBytes, remaining);
        pdu.Slice(offset, copy).CopyTo(payload.AsSpan(1, copy));
        for (int i = 1 + copy; i < 8; i++) payload[i] = PaddingByte;
        return payload;
    }

    /// <summary>
    /// Reads the little-endian PGN field that TP.CM control frames carry in bytes 5..7.
    /// Only the lower 18 bits are significant (J1939-21); reserved bits in byte 7 are masked off
    /// so non-zero padding cannot produce a value above <see cref="J1939Pgn.MaxValue"/>.
    /// </summary>
    public static uint ReadDataPgn(ReadOnlySpan<byte> tpCmPayload)
    {
        if (tpCmPayload.Length < 8) throw new ArgumentException("TP.CM payload must be 8 bytes.", nameof(tpCmPayload));
        return ((uint)tpCmPayload[5] | ((uint)tpCmPayload[6] << 8) | ((uint)tpCmPayload[7] << 16))
               & J1939Pgn.MaxValue;
    }

    private static void WriteDataPgn(byte[] payload, uint dataPgn)
    {
        payload[5] = (byte)(dataPgn & 0xFF);
        payload[6] = (byte)((dataPgn >> 8) & 0xFF);
        payload[7] = (byte)((dataPgn >> 16) & 0xFF);
    }

    private static void ValidateTotalBytes(int totalBytes)
    {
        if (totalBytes < MinTpPayloadLength || totalBytes > MaxTpPayloadLength)
            throw new ArgumentOutOfRangeException(nameof(totalBytes), totalBytes,
                $"J1939-TP payload length must be in [{MinTpPayloadLength}, {MaxTpPayloadLength}].");
    }

    private static void ValidateTotalPackets(int totalPackets)
    {
        if (totalPackets < 1 || totalPackets > 255)
            throw new ArgumentOutOfRangeException(nameof(totalPackets), totalPackets,
                "J1939-TP total packets must be in [1, 255].");
    }

    private static void ValidateDataPgn(uint dataPgn)
    {
        if (dataPgn > J1939Pgn.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dataPgn), dataPgn, "Data PGN must fit in 18 bits.");
    }
}
