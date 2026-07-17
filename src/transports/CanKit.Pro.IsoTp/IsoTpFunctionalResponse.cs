using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// A single ISO-TP PDU received from one ECU in response to a functional (1:N) request.
/// </summary>
/// <remarks>
/// Functional addressing is a broadcast mechanism (ISO 15765-2 §9): the tester transmits a
/// Single Frame on the shared functional CAN identifier, and each ECU that recognises the
/// request replies on its own physical response CAN identifier. One
/// <see cref="IsoTpFunctionalResponse"/> is produced for every Single-Frame response that
/// arrives within the collection window; multi-frame responses cannot be reassembled in
/// functional addressing (no physical response address to send Flow Control to) and are
/// dropped by the collector.
/// </remarks>
public sealed class IsoTpFunctionalResponse
{
    /// <summary>
    /// The CAN identifier on which this response arrived (the ECU's physical response CAN-ID).
    /// </summary>
    public uint SourceCanId { get; }

    /// <summary>
    /// The reassembled ISO-TP PDU payload received from the ECU.
    /// </summary>
    public byte[] Data { get; }

    internal IsoTpFunctionalResponse(uint sourceCanId, byte[] data)
    {
        SourceCanId = sourceCanId;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
}
