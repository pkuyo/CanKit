namespace CanKit.Pro.CANopen.Sdo;

/// <summary>
/// Selects which CiA 301 SDO transport the <c>SdoUploadAsync</c> /
/// <c>SdoDownloadAsync</c> APIs on <see cref="ICanOpenNode"/> should use for a single transfer.
/// </summary>
/// <remarks>
/// <see cref="Auto"/> is the recommended default and preserves the historical behaviour of the
/// public API for downloads: payloads up to four bytes go over the expedited codec
/// (CiA 301 §7.2.4.3.3), larger payloads up to <see cref="CanOpenNodeOptions.SdoBlockThresholdBytes"/>
/// go over the segmented codec (CiA 301 §7.2.4.3.5..14), and download payloads at or above the
/// threshold switch to block transfer (CiA 301 §7.2.4.3.15). Uploads cannot apply the Auto→Block
/// heuristic because the payload length is unknown until the server replies — use
/// <see cref="Block"/> explicitly for block upload. Explicit values force one specific codec:
/// they are primarily intended for tests and for callers that need to exercise a particular wire
/// encoding regardless of payload size.
/// </remarks>
public enum SdoTransferMode
{
    /// <summary>Auto-select based on payload size and <see cref="CanOpenNodeOptions"/> thresholds.</summary>
    Auto = 0,

    /// <summary>Force the expedited codec (payloads 1..4 bytes only).</summary>
    Expedited = 1,

    /// <summary>Force the segmented codec.</summary>
    Segmented = 2,

    /// <summary>Force block transfer (CiA 301 §7.2.4.3.15).</summary>
    Block = 3,
}
