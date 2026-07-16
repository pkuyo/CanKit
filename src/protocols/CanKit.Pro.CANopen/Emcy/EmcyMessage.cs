using System;

namespace CanKit.Pro.CANopen.Emcy;

/// <summary>
/// A structured CANopen Emergency (EMCY) message, per CiA 301 §7.2.7.3.
/// </summary>
/// <remarks>
/// <para>
/// The wire encoding is fixed at 8 bytes:
/// <list type="bullet">
///   <item><description>Bytes 0..1 — <c>ErrorCode</c> (little-endian).</description></item>
///   <item><description>Byte 2 — <c>ErrorRegister</c>, mirror of OD 0x1001.</description></item>
///   <item><description>Bytes 3..7 — manufacturer-specific error field (5 bytes).</description></item>
/// </list>
/// </para>
/// <para>
/// The special error code <c>0x0000</c> is the "error reset / no error" indicator described in
/// CiA 301 §7.2.7.3.1 — clients should treat it as an EMCY event just like non-zero codes and
/// let higher layers decide how to react.
/// </para>
/// </remarks>
public sealed class EmcyMessage
{
    /// <summary>Fixed on-wire size of an EMCY frame.</summary>
    public const int WireSize = 8;

    /// <summary>Length of the manufacturer-specific field.</summary>
    public const int ManufacturerFieldLength = 5;

    /// <summary>Constructs a new EMCY message. <paramref name="manufacturerSpecific"/> must be
    /// zero to five bytes long; missing bytes are treated as zero.</summary>
    public EmcyMessage(byte producerNodeId, ushort errorCode, byte errorRegister,
        ReadOnlySpan<byte> manufacturerSpecific = default)
    {
        if (manufacturerSpecific.Length > ManufacturerFieldLength)
            throw new ArgumentException(
                $"EMCY manufacturer field is at most {ManufacturerFieldLength} bytes.",
                nameof(manufacturerSpecific));
        ProducerNodeId = producerNodeId;
        ErrorCode = errorCode;
        ErrorRegister = errorRegister;
        ManufacturerSpecific = manufacturerSpecific.ToArray();
    }

    /// <summary>Node-id of the emergency producer (derived from COB-ID <c>0x080 + node-id</c>).
    /// Zero when the frame does not carry a node association (i.e. anonymous EMCY on a bus
    /// scanner).</summary>
    public byte ProducerNodeId { get; }

    /// <summary>Standardized error code (CiA 301 §7.2.7.3.1 Table 26).</summary>
    public ushort ErrorCode { get; }

    /// <summary>Content of the error register at the moment the EMCY was raised (mirror of OD
    /// <c>0x1001</c>).</summary>
    public byte ErrorRegister { get; }

    /// <summary>Manufacturer-specific 5-byte error field (may be shorter for tests; padded on
    /// encode).</summary>
    public byte[] ManufacturerSpecific { get; }

    /// <summary>Encodes the message into an 8-byte wire frame; the manufacturer-specific field
    /// is zero-padded to 5 bytes.</summary>
    public byte[] Encode()
    {
        var buf = new byte[WireSize];
        buf[0] = (byte)(ErrorCode & 0xFF);
        buf[1] = (byte)((ErrorCode >> 8) & 0xFF);
        buf[2] = ErrorRegister;
        int copy = Math.Min(ManufacturerSpecific.Length, ManufacturerFieldLength);
        for (int i = 0; i < copy; i++) buf[3 + i] = ManufacturerSpecific[i];
        return buf;
    }

    /// <summary>Decodes an EMCY frame's payload. <paramref name="producerNodeId"/> is derived
    /// from the COB-ID by the caller (<c>cob-id - 0x080</c>).</summary>
    public static EmcyMessage Decode(byte producerNodeId, ReadOnlySpan<byte> data)
    {
        if (data.Length < WireSize)
            throw new ArgumentException($"EMCY frame must be {WireSize} bytes, got {data.Length}.",
                nameof(data));
        ushort code = (ushort)(data[0] | (data[1] << 8));
        byte reg = data[2];
        var mfr = new byte[ManufacturerFieldLength];
        for (int i = 0; i < ManufacturerFieldLength; i++) mfr[i] = data[3 + i];
        return new EmcyMessage(producerNodeId, code, reg, mfr);
    }
}
