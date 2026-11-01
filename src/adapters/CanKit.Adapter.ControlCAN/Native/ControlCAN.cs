// Real ControlCAN interop (disabled in FAKE builds)
#pragma warning disable IDE0055
#pragma warning disable CS0649
#if !FAKE
using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ControlCAN.Definitions;

namespace CanKit.Adapter.ControlCAN.Native;

internal static class ControlCAN
{
    public const uint CAN_EFF_FLAG = 0x80000000U; // extended frame format
    public const uint CAN_RTR_FLAG = 0x40000000U; // remote transmission request
    public const uint CAN_ERR_FLAG = 0x20000000U; // error frame flag

    public const uint CAN_SFF_MASK = 0x000007FFU; // standard frame format mask
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU; // extended frame format mask

    private const string DllName = "controlcan";
    public const int BATCH_COUNT = 64;
    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_BOARD_INFO
    {
        public ushort hw_Version;
        public ushort fw_Version;
        public ushort dr_Version;
        public ushort in_Version;
        public ushort irq_Num;
        public byte can_Num;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)] public byte[] str_Serial;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)] public byte[] str_hw_Type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public ushort[] Reserved;
    }

    // ControlCAN.h: DWORD ExtFrame; DWORD Start; DWORD End; -> 12 bytes
    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_FILTER_RECORD
    {
        public uint ExtFrame;
        public uint Start;
        public uint End;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VCI_CAN_OBJ
    {
        public uint ID;
        public uint TimeStamp;
        public byte TimeFlag;
        public byte SendType;
        public byte RemoteFlag;
        public byte ExternFlag;
        public byte DataLen;
        public fixed byte Data[8];
        public fixed byte Reserved[3];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_INIT_CONFIG
    {
        public uint AccCode;
        public uint AccMask;
        public uint Reserved;
        public byte Filter;
        public byte Timing0;
        public byte Timing1;
        public byte Mode;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_AUTO_SEND_OBJ{
        public byte Enable;
        public byte Index;
        public uint Interval;
        public VCI_CAN_OBJ Obj;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_ERR_INFO
    {
        public uint ErrCode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] Passive_ErrData;
        public byte ArLost_ErrData;
    }

    [DllImport(DllName)]
    public static extern uint VCI_OpenDevice(uint DevType, uint DevIndex, uint Reserved);

    [DllImport(DllName)]
    public static extern uint VCI_CloseDevice(uint DevType, uint DevIndex);

    [DllImport(DllName)]
    public static extern uint VCI_InitCAN(uint DevType, uint DevIndex, uint CANIndex, ref VCI_INIT_CONFIG pInitConfig);

    [DllImport(DllName)]
    public static extern uint VCI_StartCAN(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static extern uint VCI_ResetCAN(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static extern uint VCI_ClearBuffer(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static unsafe extern uint VCI_Transmit(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ* pSend, uint Len);

    [DllImport(DllName)]
    public static extern uint VCI_Receive(uint DevType, uint DevIndex, uint CANIndex, [Out] VCI_CAN_OBJ[] pReceive, uint Len, int WaitTime);

    [DllImport(DllName)]
    public static extern uint VCI_ReadErrInfo(uint DevType, uint DevIndex, uint CANIndex, out VCI_ERR_INFO pErrInfo);

    // Some advanced device types support pre/post init configuration via SetReference.
    // RefType and data layout follow vendor documentation.
    [DllImport(DllName)]
    public static unsafe extern uint VCI_SetReference(uint DevType, uint DevIndex, uint CANIndex, uint RefType, void* pData);
}

#else
// FAKE fallback for environments without controlcan.dll
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using CanKit.Adapter.ControlCAN.Definitions;

namespace CanKit.Adapter.ControlCAN.Native;

internal static class ControlCAN
{
    public const uint CAN_EFF_FLAG = 0x80000000U; // extended frame format
    public const uint CAN_RTR_FLAG = 0x40000000U; // remote transmission request
    public const uint CAN_ERR_FLAG = 0x20000000U; // error frame flag

    public const uint CAN_SFF_MASK = 0x000007FFU; // standard frame format mask
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU; // extended frame format mask
    public const int BATCH_COUNT = 64;

    public struct VCI_BOARD_INFO { }

    // Same layout as the real binding: DWORD ExtFrame; DWORD Start; DWORD End; -> 12 bytes
    public struct VCI_FILTER_RECORD
    {
        public uint ExtFrame;
        public uint Start;
        public uint End;
    }

    public unsafe struct VCI_CAN_OBJ
    {
        public uint ID;
        public uint TimeStamp;
        public byte TimeFlag;
        public byte SendType;
        public byte RemoteFlag;
        public byte ExternFlag;
        public byte DataLen;
        public fixed byte Data[8];
        public fixed byte Reserved[3];
    }

    public struct VCI_INIT_CONFIG
    {
        public uint AccCode;
        public uint AccMask;
        public uint Reserved;
        public byte Filter; // 1=single, 0=dual; independent of the received ID format
        public byte Timing0;
        public byte Timing1;
        public byte Mode; // 0=normal, 1=listOnly
    }

    public unsafe struct VCI_AUTO_SEND_OBJ
    {
        public byte Enable;
        public byte Index;
        public uint Interval; // ms
        public VCI_CAN_OBJ Obj;
    }

    public struct VCI_ERR_INFO
    {
        public uint ErrCode;
        public byte[] Passive_ErrData;
        public byte ArLost_ErrData;
    }

    // -------- Fake world state --------
    private static class World
    {
        public static readonly object Gate = new();
        public static readonly Dictionary<(uint type, uint index), Device> Devices = new();
    }

    private sealed class Device
    {
        public uint Type;
        public uint Index;
        public bool PreserveRangeTablesOnClose;
        public int OpenCount; // Cumulative VCI_OpenDevice calls, not the number of live handles.
        public readonly Dictionary<uint, Channel> Channels = new();
    }

    private sealed class Channel
    {
        public Device Dev = null!;
        public uint Index; // 0/1
        public bool Started;
        public byte Mode; // 0=normal; 1=listOnly

        // Bit timing
        public byte T0;
        public byte T1;
        public uint RefBitValue; // via SetReference RefType=0, device-dependent

        // Filters: mask or range
        public uint AccCode;
        public uint AccMask;
        public byte FilterMode; // 1=single, 0=dual; independent of the received ID format
        public int ErrorCode;
        public int RangeWritesUntilFailure = -1;
        public bool RangeFilterEnabled;
        public int InitCount;
        public int ResetCount;
        public int ReceiveCount;
        public bool ClearRangesOnReset;
        public Action? BeforeTransmit;
        public readonly HashSet<uint> RejectedReferences = new();

        public readonly List<VCI_FILTER_RECORD> PendingRanges = new();
        public readonly List<VCI_FILTER_RECORD> ActiveRanges = new();

        // RX queue
        public readonly ConcurrentQueue<VCI_CAN_OBJ> Rx = new();

        // Periodic TX
        public readonly Dictionary<int, Timer> Periodic = new();
        public readonly Dictionary<int, (uint id, byte dlc, byte ext, byte rtr, byte[] data)> PeriodicPayload = new();
    }

    private static Device GetOrCreateDevice(uint type, uint index)
    {
        lock (World.Gate)
        {
            if (!World.Devices.TryGetValue((type, index), out var dev))
            {
                dev = new Device { Type = type, Index = index };
                World.Devices[(type, index)] = dev;
            }
            return dev;
        }
    }

    private static Channel GetOrCreateChannel(Device dev, uint canIndex)
    {
        lock (World.Gate)
        {
            if (!dev.Channels.TryGetValue(canIndex, out var ch))
            {
                ch = new Channel { Dev = dev, Index = canIndex };
                dev.Channels[canIndex] = ch;
            }
            return ch;
        }
    }

    private static Channel? TryGetPeer(Channel ch)
    {
        // For this FAKE, pair ch0<->ch1 on same device (esp. USBCAN2) when index==0
        uint peerIdx = ch.Index == 0 ? 1U : ch.Index == 1 ? 0U : 0xFFFFFFFF;
        if (peerIdx == 0xFFFFFFFF) return null;
        ch.Dev.Channels.TryGetValue(peerIdx, out var peer);
        return peer;
    }

    private static uint DecodeBitrate(Channel ch)
    {
        // Prefer explicit SetReference value when present
        if (ch.RefBitValue != 0)
        {
            // E-series encodes known presets; 4E_U uses direct bps
            var v = ch.RefBitValue;
            return v switch
            {
                0x060003 => 1_000_000U,
                0x060004 => 800_000U,
                0x060007 => 500_000U,
                0x1C0008 => 250_000U,
                0x1C0011 => 125_000U,
                0x160023 => 100_000U,
                0x1C002C => 50_000U,
                0x1600B3 => 20_000U,
                0x1C00E0 => 10_000U,
                0x1C01C1 => 5_000U,
                _ => v // assume already bps
            };
        }

        // Map T0/T1 pairs as per ControlCanBus.MapBaudRate
        return (ch.T0, ch.T1) switch
        {
            (0x00, 0x14) => 1_000_000U,
            (0x00, 0x16) => 800_000U,
            (0x00, 0x1c) => 500_000U,
            (0x01, 0x1c) => 250_000U,
            (0x03, 0x1c) => 125_000U,
            (0x04, 0x1c) => 100_000U,
            (0x09, 0x1c) => 50_000U,
            (0x18, 0x1c) => 20_000U,
            (0x31, 0x1c) => 10_000U,
            (0xBF, 0xFF) => 5_000U,
            _ => 0U,
        };
    }

    private static bool BitrateMatches(Channel a, Channel b)
    {
        var ba = DecodeBitrate(a);
        var bb = DecodeBitrate(b);
        return ba != 0 && bb != 0 && ba == bb;
    }

    private static bool IsExtended(uint canId) => (canId & CAN_EFF_FLAG) != 0;
    private static bool IsRtr(uint canId) => (canId & CAN_RTR_FLAG) != 0;

    private static uint ExtractId(uint canId)
        => IsExtended(canId) ? (canId & CAN_EFF_MASK) : (canId & CAN_SFF_MASK);

    private static uint EncodeSjaAcceptance(uint id, bool ext, bool rtr, byte data0, byte data1)
    {
        // Keep the fake's byte-register model independent of the production planner.
        // Regression tests assert literal expected matches, not only hardware/software parity.
        uint b0, b1, b2, b3;
        if (!ext)
        {
            // Standard: ACR0 = ID10..3; ACR1[7:5]=ID2..0, ACR1[4]=RTR;
            // ACR2/ACR3 compare the first two data bytes, ACR1[3:0] is unused.
            b0 = (id >> 3) & 0xFF;
            b1 = ((id & 0x7) << 5) | (rtr ? 0x10U : 0U);
            b2 = data0;
            b3 = data1;
        }
        else
        {
            // Extended: ACR0=ID28..21, ACR1=ID20..13, ACR2=ID12..5,
            // ACR3[7:3]=ID4..0, ACR3[2]=RTR, ACR3[1:0] 未用
            b0 = (id >> 21) & 0xFF;
            b1 = (id >> 13) & 0xFF;
            b2 = (id >> 5) & 0xFF;
            b3 = ((id & 0x1F) << 3) | (rtr ? 0x04U : 0U);
        }
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }


    private static unsafe bool AcceptByMask(Channel ch, in VCI_CAN_OBJ obj)
    {
        var canId = obj.ID;
        bool ext = IsExtended(canId);
        // The adapter uses dual mode only for an all-pass initialization. Its hardware
        // mask plans must use the documented 32-bit single-filter mode.
        if (ch.FilterMode == 0) return ch.AccMask == uint.MaxValue;
        var id = ExtractId(canId);
        var dataLength = IsRtr(canId) ? 0 : obj.DataLen;
        var rep = EncodeSjaAcceptance(id, ext, IsRtr(canId),
            dataLength > 0 ? obj.Data[0] : (byte)0, dataLength > 1 ? obj.Data[1] : (byte)0);
        var ignored = ch.AccMask | (ext ? 0x3U : 0x000F0000U);
        if (!ext)
        {
            // SJA1000 6.4.15.1: bytes absent due to DLC or RTR do not constrain acceptance.
            if (dataLength < 1) ignored |= 0x0000FF00U;
            if (dataLength < 2) ignored |= 0x000000FFU;
        }
        // SJA rule: (rep ^ AccCode) & ~AccMask == 0
        return ((rep ^ ch.AccCode) & ~ignored) == 0;
    }

    private static bool AcceptByRange(Channel ch, uint canId)
    {
        if (ch.ActiveRanges.Count == 0) return false;
        var id = ExtractId(canId);
        var ext = IsExtended(canId);
        foreach (var r in ch.ActiveRanges)
        {
            bool rex = r.ExtFrame != 0;
            if (rex != ext) continue;
            if (id >= (uint)r.Start && id <= (uint)r.End) return true;
        }
        return false;
    }

    private static void RouteTx(Channel tx, in VCI_CAN_OBJ obj)
    {
        var peer = TryGetPeer(tx);
        if (peer == null) return;
        if (!tx.Started || !peer.Started) return;
        if (!BitrateMatches(tx, peer)) return;

        uint id = obj.ID; // already includes flags
        bool accepted = false;

        if (peer.RangeFilterEnabled)
            accepted = AcceptByRange(peer, id);
        else
            accepted = AcceptByMask(peer, in obj);

        if (!accepted) return;

        // Enqueue clone into peer RX
        var outObj = new VCI_CAN_OBJ
        {
            ID = obj.ID,
            TimeStamp = (uint)(Environment.TickCount & 0x7FFFFFFF),
            TimeFlag = 0,
            SendType = obj.SendType,
            RemoteFlag = obj.RemoteFlag,
            ExternFlag = obj.ExternFlag,
            DataLen = obj.DataLen,
        };
        unsafe
        {
            for (int i = 0; i < Math.Min(8U, obj.DataLen); i++)
                outObj.Data[i] = obj.Data[i];
        }
        peer.Rx.Enqueue(outObj);
    }

    // -------- Public API --------
    public static uint VCI_OpenDevice(uint DevType, uint DevIndex, uint Reserved)
    {
        GetOrCreateDevice(DevType, DevIndex).OpenCount++;
        return 1;
    }

    public static uint VCI_CloseDevice(uint DevType, uint DevIndex)
    {
        lock (World.Gate)
        {
            if (World.Devices.TryGetValue((DevType, DevIndex), out var dev))
            {
                foreach (var ch in dev.Channels.Values)
                {
                    ch.Started = false;
                    lock (ch.Periodic)
                    {
                        foreach (var timer in ch.Periodic.Values) timer.Dispose();
                        ch.Periodic.Clear();
                        ch.PeriodicPayload.Clear();
                    }
                    while (ch.Rx.TryDequeue(out _)) { }
                }
                if (!dev.PreserveRangeTablesOnClose)
                    World.Devices.Remove((DevType, DevIndex));
            }
        }
        return 1;
    }

    public static uint VCI_InitCAN(uint DevType, uint DevIndex, uint CANIndex, ref VCI_INIT_CONFIG pInitConfig)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        ch.InitCount++;
        ch.Mode = pInitConfig.Mode;
        ch.T0 = pInitConfig.Timing0;
        ch.T1 = pInitConfig.Timing1;
        // Range-only devices ignore the init mask fields (ControlCAN manual, 2.5.5/2.6.3).
        var rangeOnly = (ControlCanDeviceKind)DevType is ControlCanDeviceKind.VCI_USBCAN_E_U
            or ControlCanDeviceKind.VCI_USBCAN_2E_U or ControlCanDeviceKind.VCI_USBCAN_4E_U
            or ControlCanDeviceKind.VCI_USBCAN_8E_U;
        ch.AccCode = rangeOnly ? 0 : pInitConfig.AccCode;
        ch.AccMask = rangeOnly ? uint.MaxValue : pInitConfig.AccMask;
        if (!rangeOnly && pInitConfig.Filter > 1) return 0;
        ch.FilterMode = rangeOnly ? (byte)0 : pInitConfig.Filter;
        while (ch.Rx.TryDequeue(out _)) /*Ignore*/;
        if (!rangeOnly)
        {
            ch.RangeFilterEnabled = false;
            ch.ActiveRanges.Clear();
            ch.PendingRanges.Clear();
        }
        return 1;
    }

    public static uint VCI_StartCAN(uint DevType, uint DevIndex, uint CANIndex)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        ch.Started = true;
        return 1;
    }

    public static uint VCI_ResetCAN(uint DevType, uint DevIndex, uint CANIndex)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        ch.ResetCount++;
        ch.Started = false;
        // Optional firmware behavior used to verify that opening never depends on
        // acceptance tables surviving ResetCAN. The default still exercises stale tables.
        if (ch.ClearRangesOnReset)
        {
            ch.RangeFilterEnabled = false;
            ch.PendingRanges.Clear();
            ch.ActiveRanges.Clear();
        }
        while (ch.Rx.TryDequeue(out _)) { }
        return 1;
    }

    public static uint VCI_ClearBuffer(uint DevType, uint DevIndex, uint CANIndex)
    {
        var ch = GetOrCreateChannel(GetOrCreateDevice(DevType, DevIndex), CANIndex);
        while (ch.Rx.TryDequeue(out _)) { }
        return 1;
    }

    public static unsafe uint VCI_Transmit(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ* pSend, uint Len)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        Volatile.Read(ref ch.BeforeTransmit)?.Invoke();
        if (ch.Mode == 1) return 0; // listen-only: drop TX
        uint sent = 0;
        for (int i = 0; i < Len; i++)
        {
            var obj = pSend[i];
            RouteTx(ch, in obj);
            sent++;
        }
        return sent;
    }

    public static uint VCI_GetReceiveNum(uint DevType, uint DevIndex, uint CANIndex)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        return (uint)ch.Rx.Count;
    }

    public static uint VCI_ReadErrInfo(uint DevType, uint DevIndex, uint CANIndex, out VCI_ERR_INFO pErrInfo)
    {
        var ch = GetOrCreateChannel(GetOrCreateDevice(DevType, DevIndex), CANIndex);
        pErrInfo = new VCI_ERR_INFO
        {
            ErrCode = (uint)Interlocked.Exchange(ref ch.ErrorCode, 0),
            Passive_ErrData = new byte[] { 0, 0, 0 },
            ArLost_ErrData = 0
        };
        return 1;
    }

    internal static void InjectError(uint devType, uint devIndex, uint canIndex, uint errorCode)
    {
        var ch = GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex);
        Interlocked.Exchange(ref ch.ErrorCode, (int)errorCode);
    }

    internal static void FailRangeWriteAfter(uint devType, uint devIndex, uint canIndex, int successfulWrites)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).RangeWritesUntilFailure = successfulWrites;

    internal static int PendingRangeCount(uint devType, uint devIndex, uint canIndex)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).PendingRanges.Count;

    internal static int InitializationCount(uint devType, uint devIndex, uint canIndex)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).InitCount;

    internal static int ResetCount(uint devType, uint devIndex, uint canIndex)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).ResetCount;

    internal static void ClearRangesOnReset(uint devType, uint devIndex, uint canIndex, bool clear)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).ClearRangesOnReset = clear;

    internal static void SetTransmitHook(uint devType, uint devIndex, uint canIndex, Action? hook)
        => Volatile.Write(ref GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).BeforeTransmit, hook);

    internal static int AutoSendCount(uint devType, uint devIndex, uint canIndex)
    {
        var ch = GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex);
        lock (ch.Periodic) return ch.Periodic.Count;
    }

    internal static int ReceiveCount(uint devType, uint devIndex, uint canIndex)
        => Volatile.Read(ref GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).ReceiveCount);

    internal static byte AcceptanceMode(uint devType, uint devIndex, uint canIndex)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).FilterMode;

    internal static void RejectReference(uint devType, uint devIndex, uint canIndex, uint reference)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).RejectedReferences.Add(reference);

    internal static void AllowReference(uint devType, uint devIndex, uint canIndex, uint reference)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).RejectedReferences.Remove(reference);

    internal static void PreserveRangeTablesOnClose(uint devType, uint devIndex, bool preserve)
        => GetOrCreateDevice(devType, devIndex).PreserveRangeTablesOnClose = preserve;

    internal static int OpenCount(uint devType, uint devIndex)
        => GetOrCreateDevice(devType, devIndex).OpenCount;

    internal static bool IsStarted(uint devType, uint devIndex, uint canIndex)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).Started;

    internal static int ActiveRangeCount(uint devType, uint devIndex, uint canIndex)
        => GetOrCreateChannel(GetOrCreateDevice(devType, devIndex), canIndex).ActiveRanges.Count;

    public static unsafe uint VCI_Receive(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ[] pReceive, uint Len, int WaitTime)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        Interlocked.Increment(ref ch.ReceiveCount);
        uint cnt = 0;
        while (cnt < Len && ch.Rx.TryDequeue(out var obj))
        {
            pReceive[cnt] = obj;
            cnt++;
        }
        return cnt;
    }

    public static unsafe uint VCI_SetReference(uint DevType, uint DevIndex, uint CANIndex, uint RefType, void* pData)
    {
        var dev = GetOrCreateDevice(DevType, DevIndex);
        var ch = GetOrCreateChannel(dev, CANIndex);
        if (ch.RejectedReferences.Contains(RefType)) return 0;
        // The 4E-U uses different range-filter commands (manual, 2.6.8).
        if ((ControlCanDeviceKind)DevType == ControlCanDeviceKind.VCI_USBCAN_4E_U)
        {
            if (RefType is 1 or 2 or 3) return 0;
            RefType = RefType switch { 7 => 1, 8 => 2, 10 => 3, _ => RefType };
        }
        switch (RefType)
        {
            case 0:
                // Bitrate setup: pData points to uint value (either preset code or direct bps)
                if (pData != null)
                {
                    ch.RefBitValue = *(uint*)pData;
                }
                return 1;
            case 1:
                // Add a range filter record. As in the vendor library, pData points directly
                // at one VCI_FILTER_RECORD.
                if (pData == null) return 0;
                if (ch.RangeWritesUntilFailure == 0)
                {
                    ch.RangeWritesUntilFailure = -1;
                    return 0;
                }
                if (ch.RangeWritesUntilFailure > 0) ch.RangeWritesUntilFailure--;
                ch.PendingRanges.Add(*(VCI_FILTER_RECORD*)pData);
                return 1;
            case 2:
                // Start/commit range filters
                ch.ActiveRanges.Clear();
                ch.ActiveRanges.AddRange(ch.PendingRanges);
                ch.PendingRanges.Clear();
                ch.RangeFilterEnabled = true;
                return 1;
            case 3:
                ch.ActiveRanges.Clear();
                ch.PendingRanges.Clear();
                // 4E-U has a separate stop command; clearing its table need not disable it.
                if ((ControlCanDeviceKind)DevType != ControlCanDeviceKind.VCI_USBCAN_4E_U)
                    ch.RangeFilterEnabled = false;
                return 1;
            case 9:
                ch.RangeFilterEnabled = false;
                return 1;
            case 5:
                // Periodic transmit control
                if (pData == null) return 0;
                lock (ch.Periodic)
                {
                    var pobj = (VCI_AUTO_SEND_OBJ*)pData;
                    int idx = pobj->Index;
                    if (pobj->Enable != 0)
                    {
                        // Store payload
                        var data = new byte[Math.Min(8U, pobj->Obj.DataLen)];
                        for (int i = 0; i < data.Length; i++) data[i] = pobj->Obj.Data[i];
                        ch.PeriodicPayload[idx] = (pobj->Obj.ID, pobj->Obj.DataLen, pobj->Obj.ExternFlag, pobj->Obj.RemoteFlag, data);

                        // (Re)create timer
                        if (ch.Periodic.TryGetValue(idx, out var t))
                        {
                            t.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(Math.Max(1, (int)(pobj->Interval / 2))));
                        }
                        else
                        {
                            var periodMs = Math.Max(1, (int)(pobj->Interval / 2));
                            Timer timer = new Timer(_ =>
                            {
                                if (!ch.Started) return;
                                (uint id, byte dlc, byte ext, byte rtr, byte[] data) payload;
                                lock (ch.Periodic)
                                {
                                    if (!ch.PeriodicPayload.TryGetValue(idx, out payload)) return;
                                }
                                var obj = new VCI_CAN_OBJ
                                {
                                    ID = payload.id,
                                    TimeStamp = (uint)(Environment.TickCount & 0x7FFFFFFF),
                                    TimeFlag = 0,
                                    SendType = 0,
                                    RemoteFlag = payload.rtr,
                                    ExternFlag = payload.ext,
                                    DataLen = payload.dlc,
                                };
                                unsafe { for (int i = 0; i < payload.data.Length; i++) obj.Data[i] = payload.data[i]; }
                                RouteTx(ch, in obj);
                            }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(periodMs));
                            ch.Periodic[idx] = timer;
                        }
                    }
                    else
                    {
                        if (ch.Periodic.TryGetValue(idx, out var t))
                        {
                            t.Dispose();
                            ch.Periodic.Remove(idx);
                            ch.PeriodicPayload.Remove(idx);
                        }
                    }
                }
                return 1;
            default:
                return 1;
        }
    }
}
#endif
