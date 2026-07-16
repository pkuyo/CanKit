namespace CanKit.Pro.Uds;

/// <summary>
/// UDS Service Identifiers (SIDs) supported by the CanKit.Pro.Uds MVP client.
/// </summary>
/// <remarks>
/// Values map 1:1 to the request-SID bytes defined by ISO 14229-1:2020. Positive responses use
/// the request SID plus <c>0x40</c> (per ISO 14229-1 §7.3). Negative responses are always framed
/// as <c>0x7F, requestSid, NRC</c>. The enum only covers the services the MVP client actually
/// implements; unknown SIDs are still supported via the <see cref="IUdsClient.SendRawAsync"/>
/// escape hatch.
/// </remarks>
public enum UdsServiceId : byte
{
    /// <summary>DiagnosticSessionControl — ISO 14229-1 §9.2 (SRS FR-UDS-001).</summary>
    DiagnosticSessionControl = 0x10,

    /// <summary>ECUReset — ISO 14229-1 §9.1 (SRS FR-UDS-005).</summary>
    EcuReset = 0x11,

    /// <summary>ReadDataByIdentifier — ISO 14229-1 §9.3 (SRS FR-UDS-002 / FR-UDS-011).</summary>
    ReadDataByIdentifier = 0x22,

    /// <summary>SecurityAccess — ISO 14229-1 §9.4 (SRS FR-UDS-006).</summary>
    SecurityAccess = 0x27,

    /// <summary>WriteDataByIdentifier — ISO 14229-1 §9.6 (SRS FR-UDS-003).</summary>
    WriteDataByIdentifier = 0x2E,

    /// <summary>RoutineControl — ISO 14229-1 §9.10 (SRS FR-UDS-004).</summary>
    RoutineControl = 0x31,

    /// <summary>TesterPresent — ISO 14229-1 §9.12 (SRS FR-UDS-007).</summary>
    TesterPresent = 0x3E,
}

/// <summary>
/// DiagnosticSessionControl (0x10) session type sub-functions from ISO 14229-1 §9.2.2.
/// </summary>
public enum UdsSessionType : byte
{
    /// <summary>defaultSession (0x01) — cleared to on power-on.</summary>
    Default = 0x01,

    /// <summary>programmingSession (0x02) — for flash/upload/download flows.</summary>
    Programming = 0x02,

    /// <summary>extendedDiagnosticSession (0x03) — expands the accessible service set.</summary>
    Extended = 0x03,

    /// <summary>safetySystemDiagnosticSession (0x04).</summary>
    SafetySystem = 0x04,
}

/// <summary>
/// ECUReset (0x11) reset-type sub-functions from ISO 14229-1 §9.1.2.
/// </summary>
public enum UdsEcuResetType : byte
{
    /// <summary>hardReset (0x01) — cold-boot equivalent.</summary>
    HardReset = 0x01,

    /// <summary>keyOffOnReset (0x02) — simulates ignition off/on.</summary>
    KeyOffOnReset = 0x02,

    /// <summary>softReset (0x03) — application-level reset.</summary>
    SoftReset = 0x03,

    /// <summary>enableRapidPowerShutDown (0x04).</summary>
    EnableRapidPowerShutDown = 0x04,

    /// <summary>disableRapidPowerShutDown (0x05).</summary>
    DisableRapidPowerShutDown = 0x05,
}

/// <summary>
/// RoutineControl (0x31) sub-functions from ISO 14229-1 §9.10.2.
/// </summary>
public enum UdsRoutineControlType : byte
{
    /// <summary>startRoutine (0x01).</summary>
    StartRoutine = 0x01,

    /// <summary>stopRoutine (0x02).</summary>
    StopRoutine = 0x02,

    /// <summary>requestRoutineResults (0x03).</summary>
    RequestRoutineResults = 0x03,
}
