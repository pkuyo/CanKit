using System;

namespace CanKit.Pro.CANopen;

/// <summary>
/// CiA 301 pre-defined connection set: helpers that translate CANopen node identifiers into the
/// 11-bit COB-IDs used for NMT, SYNC, EMCY, PDO, SDO and heartbeat traffic.
/// </summary>
/// <remarks>
/// Constants and helpers only — no state, no allocation. The layout mirrors CiA 301 §7.2:
/// <list type="bullet">
///   <item><description>NMT master command frame — <c>0x000</c></description></item>
///   <item><description>SYNC — <c>0x080</c></description></item>
///   <item><description>EMCY — <c>0x080 + node-id</c></description></item>
///   <item><description>TPDO1..4 — <c>0x180/0x280/0x380/0x480 + node-id</c></description></item>
///   <item><description>RPDO1..4 — <c>0x200/0x300/0x400/0x500 + node-id</c></description></item>
///   <item><description>SDO server → client — <c>0x580 + node-id</c></description></item>
///   <item><description>SDO client → server — <c>0x600 + node-id</c></description></item>
///   <item><description>NMT error control (heartbeat / bootup) — <c>0x700 + node-id</c></description></item>
/// </list>
/// </remarks>
public static class CanOpenCobId
{
    /// <summary>Smallest legal CANopen node identifier (CiA 301 §7.2.4).</summary>
    public const byte MinNodeId = 1;

    /// <summary>Largest legal CANopen node identifier (CiA 301 §7.2.4).</summary>
    public const byte MaxNodeId = 127;

    /// <summary>NMT master command COB-ID (single-broadcast, no node offset).</summary>
    public const uint NmtCommand = 0x000;

    /// <summary>SYNC COB-ID.</summary>
    public const uint Sync = 0x080;

    /// <summary>Base COB-ID for EMCY frames: <c>0x080 + node-id</c>.</summary>
    public const uint EmcyBase = 0x080;

    /// <summary>Base COB-ID for TPDO1: <c>0x180 + node-id</c>.</summary>
    public const uint Tpdo1Base = 0x180;

    /// <summary>Base COB-ID for RPDO1: <c>0x200 + node-id</c>.</summary>
    public const uint Rpdo1Base = 0x200;

    /// <summary>Base COB-ID for TPDO2: <c>0x280 + node-id</c>.</summary>
    public const uint Tpdo2Base = 0x280;

    /// <summary>Base COB-ID for RPDO2: <c>0x300 + node-id</c>.</summary>
    public const uint Rpdo2Base = 0x300;

    /// <summary>Base COB-ID for TPDO3: <c>0x380 + node-id</c>.</summary>
    public const uint Tpdo3Base = 0x380;

    /// <summary>Base COB-ID for RPDO3: <c>0x400 + node-id</c>.</summary>
    public const uint Rpdo3Base = 0x400;

    /// <summary>Base COB-ID for TPDO4: <c>0x480 + node-id</c>.</summary>
    public const uint Tpdo4Base = 0x480;

    /// <summary>Base COB-ID for RPDO4: <c>0x500 + node-id</c>.</summary>
    public const uint Rpdo4Base = 0x500;

    /// <summary>Base COB-ID for SDO server → client responses: <c>0x580 + node-id</c>.</summary>
    public const uint SdoTxBase = 0x580;

    /// <summary>Base COB-ID for SDO client → server requests: <c>0x600 + node-id</c>.</summary>
    public const uint SdoRxBase = 0x600;

    /// <summary>Base COB-ID for NMT error control (heartbeat + bootup): <c>0x700 + node-id</c>.</summary>
    public const uint HeartbeatBase = 0x700;

    /// <summary>Validates that <paramref name="nodeId"/> falls in the legal CiA 301 range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="nodeId"/> is
    /// outside <c>[1, 127]</c>.</exception>
    public static void ValidateNodeId(byte nodeId)
    {
        if (nodeId < MinNodeId || nodeId > MaxNodeId)
            throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId,
                $"CANopen node-id must be in [{MinNodeId}, {MaxNodeId}].");
    }

    /// <summary>Returns <c>0x580 + node-id</c>, the SDO server → client (SDO Tx) COB-ID.</summary>
    public static uint SdoTx(byte nodeId) => SdoTxBase + nodeId;

    /// <summary>Returns <c>0x600 + node-id</c>, the SDO client → server (SDO Rx) COB-ID.</summary>
    public static uint SdoRx(byte nodeId) => SdoRxBase + nodeId;

    /// <summary>Returns <c>0x700 + node-id</c>, the heartbeat / bootup COB-ID.</summary>
    public static uint Heartbeat(byte nodeId) => HeartbeatBase + nodeId;

    /// <summary>Returns <c>0x080 + node-id</c>, the EMCY COB-ID.</summary>
    public static uint Emcy(byte nodeId) => EmcyBase + nodeId;

    /// <summary>Returns the default TPDO COB-ID for <paramref name="pdoIndex"/> (1..4) and
    /// <paramref name="nodeId"/> according to the CiA 301 pre-defined connection set.</summary>
    public static uint TpdoDefault(byte nodeId, int pdoIndex) => pdoIndex switch
    {
        1 => Tpdo1Base + nodeId,
        2 => Tpdo2Base + nodeId,
        3 => Tpdo3Base + nodeId,
        4 => Tpdo4Base + nodeId,
        _ => throw new ArgumentOutOfRangeException(nameof(pdoIndex), pdoIndex, "PDO index must be 1..4."),
    };

    /// <summary>Returns the default RPDO COB-ID for <paramref name="pdoIndex"/> (1..4) and
    /// <paramref name="nodeId"/> according to the CiA 301 pre-defined connection set.</summary>
    public static uint RpdoDefault(byte nodeId, int pdoIndex) => pdoIndex switch
    {
        1 => Rpdo1Base + nodeId,
        2 => Rpdo2Base + nodeId,
        3 => Rpdo3Base + nodeId,
        4 => Rpdo4Base + nodeId,
        _ => throw new ArgumentOutOfRangeException(nameof(pdoIndex), pdoIndex, "PDO index must be 1..4."),
    };
}
