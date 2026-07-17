namespace CanKit.Pro.Uds;

/// <summary>
/// Named subset of Negative Response Codes from ISO 14229-1:2020 Table A.1. Values not listed
/// here are still delivered as their raw <see cref="byte"/> value on
/// <see cref="UdsNegativeResponseException.Code"/>; the enum only names the ones the MVP client
/// documents behaviour for.
/// </summary>
public enum UdsNegativeResponseCode : byte
{
    /// <summary>generalReject (0x10).</summary>
    GeneralReject = 0x10,

    /// <summary>serviceNotSupported (0x11).</summary>
    ServiceNotSupported = 0x11,

    /// <summary>subFunctionNotSupported (0x12).</summary>
    SubFunctionNotSupported = 0x12,

    /// <summary>incorrectMessageLengthOrInvalidFormat (0x13).</summary>
    IncorrectMessageLengthOrInvalidFormat = 0x13,

    /// <summary>busyRepeatRequest (0x21).</summary>
    BusyRepeatRequest = 0x21,

    /// <summary>conditionsNotCorrect (0x22).</summary>
    ConditionsNotCorrect = 0x22,

    /// <summary>requestSequenceError (0x24).</summary>
    RequestSequenceError = 0x24,

    /// <summary>requestOutOfRange (0x31).</summary>
    RequestOutOfRange = 0x31,

    /// <summary>securityAccessDenied (0x33).</summary>
    SecurityAccessDenied = 0x33,

    /// <summary>invalidKey (0x35).</summary>
    InvalidKey = 0x35,

    /// <summary>exceedNumberOfAttempts (0x36).</summary>
    ExceedNumberOfAttempts = 0x36,

    /// <summary>requiredTimeDelayNotExpired (0x37).</summary>
    RequiredTimeDelayNotExpired = 0x37,

    /// <summary>uploadDownloadNotAccepted (0x70).</summary>
    UploadDownloadNotAccepted = 0x70,

    /// <summary>transferDataSuspended (0x71).</summary>
    TransferDataSuspended = 0x71,

    /// <summary>generalProgrammingFailure (0x72).</summary>
    GeneralProgrammingFailure = 0x72,

    /// <summary>wrongBlockSequenceCounter (0x73).</summary>
    WrongBlockSequenceCounter = 0x73,

    /// <summary>
    /// requestCorrectlyReceived-ResponsePending (0x78). Server acknowledges the request but needs
    /// more time; the client MUST restart its P2* timer and keep waiting until the final response
    /// arrives or the P2* budget is exhausted (SRS FR-UDS-009, ISO 14229-1 §7.3.3).
    /// </summary>
    RequestCorrectlyReceivedResponsePending = 0x78,

    /// <summary>subFunctionNotSupportedInActiveSession (0x7E).</summary>
    SubFunctionNotSupportedInActiveSession = 0x7E,

    /// <summary>serviceNotSupportedInActiveSession (0x7F).</summary>
    ServiceNotSupportedInActiveSession = 0x7F,
}
