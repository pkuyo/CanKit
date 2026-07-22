using System;
using System.Collections.Generic;
using CanKit.Pro.CANopen.Pdo;
using CanKit.Pro.CANopen.Sdo;

namespace CanKit.Pro.CANopen;

/// <summary>
/// Dynamic PDO mapping over SDO (FR-CO-005 / CiA 301 §7.2.4.6 "PDO mapping parameter",
/// Table 61): SDO access to the mapping records 0x1600..0x1603 (RPDO) and 0x1A00..0x1A03
/// (TPDO) of this node.
/// </summary>
/// <remarks>
/// <para>
/// Write protocol (per CiA 301): the master deactivates the mapping by writing sub0 = 0,
/// writes the 32-bit mapping entries (<c>index &lt;&lt; 16 | subindex &lt;&lt; 8 | bit-length</c>,
/// little-endian) to sub1..subN, then activates by writing sub0 = N. Writing an entry while
/// the mapping is active (no preceding sub0 = 0) is rejected with
/// <see cref="SdoAbortCode.UnsupportedAccess"/>; a commit whose staged entry count does not
/// match N is rejected with a length abort.
/// </para>
/// <para>
/// MVP rules (documented): entries must be byte-aligned (bit length 8..64, multiple of 8 —
/// the same constraint <see cref="PdoMappingEntry"/> enforces), the assembled payload must
/// not exceed 8 bytes, and every mapped target must exist in the local OD and be accessible
/// in the PDO's direction (TPDO reads, RPDO writes). Staged entries apply in the order they
/// were written; the previously active mapping stays in effect until a new mapping commits,
/// so a master that abandons a reconfiguration halfway does not leave the PDO unmapped.
/// Reads of the mapping records (upload) answer sub0 with the active entry count and
/// sub1..subN with the encoded 32-bit entries. All handlers run on the node's actor loop
/// (SDO server context), so the staging table needs no synchronization.
/// </para>
/// </remarks>
internal sealed partial class CanOpenNode
{
    /// <summary>CiA 301 mapping-record capacity (sub1..sub64).</summary>
    private const int MaxPdoMappingSubindex = 64;

    // Staged 32-bit mapping entries per mapping-object index while a reconfiguration session
    // is open (sub0 written with 0, not yet committed with the final count). Actor-confined.
    private readonly Dictionary<ushort, List<uint>> _pdoMappingStaging = new();

    private static bool IsPdoMappingIndex(ushort index, out bool isTpdo, out int pdoIndex)
    {
        if (index is >= 0x1600 and <= 0x1603)
        {
            isTpdo = false;
            pdoIndex = index - 0x1600 + 1;
            return true;
        }
        if (index is >= 0x1A00 and <= 0x1A03)
        {
            isTpdo = true;
            pdoIndex = index - 0x1A00 + 1;
            return true;
        }
        isTpdo = false;
        pdoIndex = 0;
        return false;
    }

    private void HandlePdoMappingSdoRequest(ushort index, byte subindex, byte cs, byte[] data,
        bool isTpdo, int pdoIndex)
    {
        // A fresh initiate against a mapping record supersedes any open segmented or block
        // session, exactly like the generic server path does (CiA 301 §7.2.4.3.4).
        AbortSupersededServerSession();
        AbortSupersededBlockServerSession();

        if (cs == SdoFrames.CcsUploadInit)
        {
            HandlePdoMappingUpload(index, subindex, isTpdo, pdoIndex);
            return;
        }

        // Mapping records are written expedited-only. Mirror the generic server path, which
        // treats every 0x2X initiate except the segmented 0x21 as expedited (MVP
        // simplification — see HandleServerDownloadInit).
        if (cs == SdoFrames.CcsDownloadInitSegmented || (cs & 0xE0) != SdoFrames.CcsDownloadInitExpeditedBase)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.CommandSpecifierInvalid);
            return;
        }

        var payload = SdoFrames.ReadExpeditedPayload(data);
        if (subindex == 0)
        {
            HandlePdoMappingSubZeroWrite(index, payload, isTpdo, pdoIndex);
        }
        else
        {
            HandlePdoMappingEntryWrite(index, subindex, payload, isTpdo);
        }
    }

    private void HandlePdoMappingUpload(ushort index, byte subindex, bool isTpdo, int pdoIndex)
    {
        var entries = CurrentMappingEntries(isTpdo, pdoIndex);
        uint value;
        if (subindex == 0)
        {
            value = (uint)entries.Count;
        }
        else if (subindex <= entries.Count)
        {
            value = EncodeMappingEntry(entries[subindex - 1]);
        }
        else
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.SubIndexDoesNotExist);
            return;
        }

        // Expedited upload response with a 4-byte value (mirrors HandleServerUploadInit).
        var buf = new byte[8];
        buf[0] = SdoFrames.ScsUploadInitExpeditedBase | 0x03;
        buf[1] = (byte)(index & 0xFF);
        buf[2] = (byte)((index >> 8) & 0xFF);
        buf[3] = subindex;
        buf[4] = (byte)(value & 0xFF);
        buf[5] = (byte)((value >> 8) & 0xFF);
        buf[6] = (byte)((value >> 16) & 0xFF);
        buf[7] = (byte)((value >> 24) & 0xFF);
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), buf);
    }

    private void HandlePdoMappingSubZeroWrite(ushort index, byte[] payload, bool isTpdo, int pdoIndex)
    {
        // Sub0 is U8 (the mapped-object count); any other width is a length mismatch.
        if (payload.Length != 1)
        {
            SendSdoServerAbort(index, 0, SdoAbortCode.DataTypeLengthMismatch);
            return;
        }

        byte count = payload[0];
        if (count == 0)
        {
            // Deactivate: opens a reconfiguration session. The active mapping stays in effect
            // until a new one commits (see class remarks).
            _pdoMappingStaging[index] = new List<uint>();
            AckPdoMappingDownload(index, 0);
            return;
        }
        if (count > MaxPdoMappingSubindex)
        {
            SendSdoServerAbort(index, 0, SdoAbortCode.LengthTooHigh);
            return;
        }
        if (!_pdoMappingStaging.TryGetValue(index, out var staged))
        {
            // CiA 301 requires deactivating the mapping (sub0 = 0) before re-activation.
            SendSdoServerAbort(index, 0, SdoAbortCode.UnsupportedAccess);
            return;
        }
        if (staged.Count != count)
        {
            SendSdoServerAbort(index, 0,
                staged.Count < count ? SdoAbortCode.LengthTooLow : SdoAbortCode.LengthTooHigh);
            return;
        }

        var mapping = new PdoMapping();
        try
        {
            foreach (var raw in staged)
            {
                mapping.Add(new PdoMappingEntry(
                    (ushort)((raw >> 16) & 0xFFFF), (byte)((raw >> 8) & 0xFF), (byte)(raw & 0xFF)));
            }
        }
        catch (InvalidOperationException)
        {
            // PdoMapping.Validate's 8-byte guard (already pre-checked per entry — defense).
            SendSdoServerAbort(index, 0, SdoAbortCode.PdoMappingLengthExceeded);
            return;
        }

        _pdoMappingStaging.Remove(index);
        if (isTpdo)
        {
            ApplyTpdoMappingFromSdo(pdoIndex, mapping);
        }
        else
        {
            ApplyRpdoMappingFromSdo(pdoIndex, mapping);
        }
        AckPdoMappingDownload(index, 0);
    }

    private void HandlePdoMappingEntryWrite(ushort index, byte subindex, byte[] payload, bool isTpdo)
    {
        if (subindex > MaxPdoMappingSubindex)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.SubIndexDoesNotExist);
            return;
        }
        if (!_pdoMappingStaging.TryGetValue(index, out var staged))
        {
            // Mapping is active; CiA 301 requires writing sub0 = 0 before touching entries.
            SendSdoServerAbort(index, subindex, SdoAbortCode.UnsupportedAccess);
            return;
        }
        if (payload.Length != 4)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.DataTypeLengthMismatch);
            return;
        }

        uint raw = (uint)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24));
        var entryIndex = (ushort)((raw >> 16) & 0xFFFF);
        var entrySub = (byte)((raw >> 8) & 0xFF);
        var bitLength = (byte)(raw & 0xFF);

        // MVP maps byte-aligned fields only (PdoMappingEntry enforces the same constraint).
        if (bitLength == 0 || bitLength > 64 || bitLength % 8 != 0)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.DataTypeLengthMismatch);
            return;
        }
        // The mapped target must exist in the local OD and be accessible in the PDO's
        // direction: TPDO reads it (ReadOnly), RPDO writes it (WriteOnly).
        if (!_od.TryGet(entryIndex, entrySub, out var target))
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.ObjectDoesNotExist);
            return;
        }
        var needed = isTpdo ? OdAccess.ReadOnly : OdAccess.WriteOnly;
        if ((target.Access & needed) == 0)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.ObjectCannotBeMapped);
            return;
        }
        // The assembled payload must not exceed the 8-byte classic CAN limit.
        int total = 0;
        foreach (var e in staged)
        {
            total += (int)(e & 0xFF) / 8;
        }
        if (total + bitLength / 8 > 8)
        {
            SendSdoServerAbort(index, subindex, SdoAbortCode.PdoMappingLengthExceeded);
            return;
        }

        staged.Add(raw);
        AckPdoMappingDownload(index, subindex);
    }

    private void AckPdoMappingDownload(ushort index, byte subindex)
    {
        var resp = new byte[8];
        resp[0] = SdoFrames.ScsDownloadInitAck;
        resp[1] = (byte)(index & 0xFF);
        resp[2] = (byte)((index >> 8) & 0xFF);
        resp[3] = subindex;
        _ = SendControlFrame(CanOpenCobId.SdoTx(_nodeId), resp);
    }

    private IReadOnlyList<PdoMappingEntry> CurrentMappingEntries(bool isTpdo, int pdoIndex)
    {
        if (isTpdo)
        {
            return _tpdos.TryGetValue(pdoIndex, out var t)
                ? t.Mapping.Entries
                : Array.Empty<PdoMappingEntry>();
        }
        foreach (var kv in _rpdosByCobId)
        {
            if (kv.Value.PdoIndex == pdoIndex)
            {
                return kv.Value.Mapping.Entries;
            }
        }
        return Array.Empty<PdoMappingEntry>();
    }

    private static uint EncodeMappingEntry(PdoMappingEntry e)
        => ((uint)e.Index << 16) | ((uint)e.Subindex << 8) | e.BitLength;

    // Replaces the mapping of a configured TPDO slot in place (COB-ID, transmission mode and
    // timer interval survive), or creates a default EventDriven slot when the application
    // never configured one. Runs on the actor loop (SDO server context).
    private void ApplyTpdoMappingFromSdo(int pdoIndex, PdoMapping mapping)
    {
        if (_tpdos.TryGetValue(pdoIndex, out var existing))
        {
            existing.EventTimerHandle?.Dispose();
            var replaced = new TpdoConfig(pdoIndex, existing.CobId, mapping,
                existing.Transmission, existing.EventTimerInterval);
            _tpdos[pdoIndex] = replaced;
            if (replaced.Transmission == TpdoTransmission.EventTimer)
            {
                ScheduleTpdoEventTimer(replaced);
            }
        }
        else
        {
            _tpdos[pdoIndex] = new TpdoConfig(pdoIndex, CanOpenCobId.TpdoDefault(_nodeId, pdoIndex),
                mapping, TpdoTransmission.EventDriven, _options.DefaultTpdoEventTimerInterval);
        }
        RebuildCosRelevantEntries();
    }

    // Replaces the mapping of a configured RPDO slot (keeping its COB-ID), or installs a new
    // slot at the default COB-ID. Runs on the actor loop (SDO server context).
    private void ApplyRpdoMappingFromSdo(int pdoIndex, PdoMapping mapping)
    {
        uint cobId = CanOpenCobId.RpdoDefault(_nodeId, pdoIndex);
        foreach (var kv in _rpdosByCobId)
        {
            if (kv.Value.PdoIndex == pdoIndex)
            {
                cobId = kv.Key;
                break;
            }
        }

        // Remove any previous entry for this slot (same cleanup as ConfigureRpdo.Apply).
        uint[] existingKeys = new uint[_rpdosByCobId.Count];
        int i = 0;
        foreach (var kv in _rpdosByCobId)
        {
            existingKeys[i++] = kv.Key;
        }
        foreach (var key in existingKeys)
        {
            if (_rpdosByCobId[key].PdoIndex == pdoIndex)
            {
                _rpdosByCobId.Remove(key);
            }
        }
        _rpdosByCobId[cobId] = new RpdoConfig(pdoIndex, cobId, mapping);
    }
}
