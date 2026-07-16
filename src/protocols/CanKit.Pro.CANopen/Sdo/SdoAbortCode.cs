namespace CanKit.Pro.CANopen.Sdo;

/// <summary>
/// A subset of the CiA 301 §7.2.4.3 Table 45 SDO abort codes needed by the MVP. Values are the
/// exact 32-bit codes carried in an SDO Abort frame's data bytes 4..7 (little-endian).
/// </summary>
/// <remarks>
/// Only the codes actually emitted or matched by the MVP are enumerated. The wider CANopen
/// standard defines dozens more; unrecognized abort codes coming in from a remote peer are still
/// preserved as raw <c>uint</c> in <see cref="SdoAbortException"/> so callers see the exact
/// vendor-specified code.
/// </remarks>
public enum SdoAbortCode : uint
{
    /// <summary>Toggle bit not alternated (segmented transfer protocol violation).</summary>
    ToggleBitNotAlternated = 0x05030000u,

    /// <summary>SDO protocol timed out.</summary>
    SdoProtocolTimedOut = 0x05040000u,

    /// <summary>Client/server command specifier not valid or unknown.</summary>
    CommandSpecifierInvalid = 0x05040001u,

    /// <summary>Invalid block size (only used by block transfer).</summary>
    InvalidBlockSize = 0x05040002u,

    /// <summary>Unsupported access to an object.</summary>
    UnsupportedAccess = 0x06010000u,

    /// <summary>Attempt to read a write-only object.</summary>
    AttemptReadWriteOnly = 0x06010001u,

    /// <summary>Attempt to write a read-only object.</summary>
    AttemptWriteReadOnly = 0x06010002u,

    /// <summary>Object does not exist in the object dictionary.</summary>
    ObjectDoesNotExist = 0x06020000u,

    /// <summary>Data type does not match — length of service parameter too high.</summary>
    LengthTooHigh = 0x06070012u,

    /// <summary>Data type does not match — length of service parameter too low.</summary>
    LengthTooLow = 0x06070013u,

    /// <summary>Sub-index does not exist.</summary>
    SubIndexDoesNotExist = 0x06090011u,

    /// <summary>General error / unspecified.</summary>
    General = 0x08000000u,
}
