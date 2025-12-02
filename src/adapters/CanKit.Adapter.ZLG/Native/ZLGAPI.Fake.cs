// FAKE ZLG zlgcan backend for unit tests
// @formatter:off
#nullable disable
#pragma warning disable IDE0055
#pragma warning disable CS0649
#if FAKE
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Native;

public static class ZLGCAN
{
    // ---- Public constants (mirrors real ZLGAPI.cs) ----
    public const uint CAN_EFF_FLAG = 0x80000000U; // extended frame format
    public const uint CAN_RTR_FLAG = 0x40000000U; // remote request
    public const uint CAN_ERR_FLAG = 0x20000000U; // error frame flag
    public const uint CAN_SFF_MASK = 0x000007FFU; // standard id mask
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU; // extended id mask
    public const uint CAN_ERR_MASK = 0x1FFFFFFFU; // error mask

    public const int TX_ECHO_FLAG = 0x20;
    public const int CANFD_BRS    = 0x01;
    public const int CANFD_ESI    = 0x02;

    public const int BATCH_COUNT = 64;

    // ZLG device types (subset used in tests)
    public static readonly UInt32 ZCAN_USBCAN2                 = 4;
    public static readonly UInt32 ZCAN_PCIE_CANFD_200U         = 39;
    public static readonly UInt32 ZCAN_USBCANFD_200U           = 41;

    // Status OK
    private const uint OK = 1;

    // ---- Internal fake world ----
    private static class World
    {
        public static readonly object Gate = new();
        public static IntPtr NextDev = (IntPtr)0x1001;
        public static IntPtr NextCh  = (IntPtr)0x2001;
        public static readonly Dictionary<IntPtr, Device> Devices = new();
        public static readonly Dictionary<IntPtr, Channel> Channels = new();
        public static readonly DateTime Epoch = DateTime.UtcNow;
    }

    private sealed class Device
    {
        public UInt32 Type;
        public UInt32 Index;
        public IntPtr Handle;
        public readonly Dictionary<int, Channel> Channels = new();
        public readonly ConcurrentQueue<ZCANDataObj> MergeRx = new();
        public AutoResetEvent MergeRxEvt = new AutoResetEvent(false);
        public bool BusUsageEnabled;
        public uint BusUsagePeriodMs = 200;
        public ulong LastBusUsageTs = 0;
        public uint BusUsagePermille = 0; // 0..10000; scaled by 100

        public bool MergeReceive;
    }

    private sealed class Channel
    {
        public Device Dev = null!;
        public int Index;
        public IntPtr Handle;
        public bool Started;
        public ChannelWorkMode WorkMode = ChannelWorkMode.Normal;

        // Protocol & timing
        public bool FdEnabled;
        public uint ClassicBaud;         // for classic-only devices
        public uint ArbBaud;             // arbitration bitrate for FD-capable
        public uint DataBaud;            // data bitrate for FD

        // Filters (hardware simulation). Either mask or range; range supports multiple records.
        public bool HasMaskFilter;
        public uint MaskAccCode;
        public uint MaskAccMask;
        public CanFilterIDType MaskIdType;

        // Active range rules (after filter_ack)
        public readonly List<RangeRule> RangeRules = new();
        // Pending range rules (collected before filter_ack)
        public readonly List<RangeRule> PendingRangeRules = new();
        // Sticky pending idType set by filter_mode
        public bool PendingHasIdType;
        public CanFilterIDType PendingIdType;
        // Temp start for current pending record; a subsequent filter_end finalizes a record
        public bool PendingHasStart;
        public uint PendingStart;
        // RX queues
        public readonly ConcurrentQueue<ZCAN_Receive_Data> RxClassic = new();
        public readonly ConcurrentQueue<ZCAN_ReceiveFD_Data> RxFd = new();
        public readonly AutoResetEvent RxEvtClassic = new(false);
        public readonly AutoResetEvent RxEvtFd = new(false);


        // Error counters
        public byte Rec; // receive error counter
        public byte Tec; // transmit error counter
    }

    private sealed class RangeRule
    {
        public CanFilterIDType IdType;
        public uint Start;
        public uint End;
    }

    // ---- Helpers ----
    private static bool IsLinkedPair(UInt32 devType, UInt32 index, int ch)
    {
        if (index != 0) return false;
        if (devType == ZCAN_USBCANFD_200U || devType == ZCAN_USBCAN2 || devType == ZCAN_PCIE_CANFD_200U)
        {
            return ch is 0 or 1;
        }
        return false;
    }

    private static int LinkedPeerIndex(int ch) => ch switch
    {
        0 => 1,
        1 => 0,
        _ => -1
    };

    private static bool IsExtended(uint can_id) => (can_id & CAN_EFF_FLAG) != 0;
    private static bool IsRtr(uint can_id) => (can_id & CAN_RTR_FLAG) != 0;
    private static uint ExtractId(uint can_id)
        => (can_id & CAN_EFF_FLAG) != 0 ? (can_id & CAN_EFF_MASK) : (can_id & CAN_SFF_MASK);

    private static ulong NowUs()
    {
        var delta = DateTime.UtcNow - World.Epoch;
        return (ulong)(delta.TotalMilliseconds * 1000.0);
    }

    private static bool AcceptByFilter(Channel ch, uint can_id)
    {
        var id = ExtractId(can_id);
        var ext = IsExtended(can_id);

        if (ch.HasMaskFilter)
        {
            return ((can_id ^ ch.MaskAccCode) & ~ch.MaskAccMask) == 0;
        }

        if (ch.RangeRules.Count > 0)
        {
            foreach (var rr in ch.RangeRules)
            {
                if ((rr.IdType == CanFilterIDType.Extend) != ext) continue;
                if (id >= rr.Start && id <= rr.End) return true;
            }
            return false;
        }

        // No hardware filter configured => accept all
        return true;
    }

    private static bool BitrateMatches(Channel rx, Channel tx, bool isFd)
    {
        if (isFd)
        {
            if (!rx.FdEnabled || !tx.FdEnabled) return false;
            uint rArb = rx.ArbBaud != 0 ? rx.ArbBaud : rx.ClassicBaud;
            uint tArb = tx.ArbBaud != 0 ? tx.ArbBaud : tx.ClassicBaud;
            if (rArb == 0 || tArb == 0 || rArb != tArb) return false;
            if (rx.DataBaud == 0 || tx.DataBaud == 0 || rx.DataBaud != tx.DataBaud) return false;
            return true;
        }
        else
        {
            uint rArb = rx.ArbBaud != 0 ? rx.ArbBaud : rx.ClassicBaud;
            uint tArb = tx.ArbBaud != 0 ? tx.ArbBaud : tx.ClassicBaud;
            return rArb != 0 && tArb != 0 && rArb == tArb;
        }
    }

    private static unsafe void EnqueueClassic(Channel rx, uint can_id, byte dlc, ReadOnlySpan<byte> data)
    {
        var rd = new ZCAN_Receive_Data();
        rd.timestamp = NowUs();
        rd.frame.can_id = can_id;
        rd.frame.can_dlc = dlc;
        int copy = Math.Min(data.Length, 8);
        for (int i = 0; i < copy; i++) rd.frame.data[i] = data[i];
        rx.RxClassic.Enqueue(rd);
        rx.RxEvtClassic.Set();
    }

    private static unsafe void EnqueueFd(Channel rx, uint can_id, byte len, byte flags, ReadOnlySpan<byte> data)
    {
        var rd = new ZCAN_ReceiveFD_Data();
        rd.timestamp = NowUs();
        rd.frame.can_id = can_id;
        rd.frame.len = len;
        rd.frame.flags = flags;
        int copy = Math.Min(data.Length, 64);
        for (int i = 0; i < copy; i++) rd.frame.data[i] = data[i];
        rx.RxFd.Enqueue(rd);
        rx.RxEvtFd.Set();
    }

    private static unsafe void EnqueueMerge(Device dev, byte chnl, bool isFd, uint can_id, byte lenOrDlc, byte fdFlags, ReadOnlySpan<byte> data)
    {
        var obj = new ZCANDataObj();
        obj.dataType = 1; // CAN/CAN FD
        obj.chnl = chnl;
        obj.flag = 0;
        obj.data.fdData.timeStamp = NowUs();
        obj.data.fdData.flag = 0;
        obj.data.fdData.frameType = isFd ? 1u : 0u;
        obj.data.fdData.frame.can_id = can_id;
        obj.data.fdData.frame.len = lenOrDlc;
        obj.data.fdData.frame.flags = fdFlags;
        int copy = Math.Min(data.Length, isFd ? 64 : 8);
        for (int i = 0; i < copy; i++) obj.data.fdData.frame.data[i] = data[i];
        dev.MergeRx.Enqueue(obj);
        dev.MergeRxEvt.Set();
    }

    private static void RouteTx(Device dev, Channel txCh, bool isFd, uint can_id, byte lenOrDlc, byte fdFlags, ReadOnlySpan<byte> data)
    {
        if (!txCh.Started) return;

        // echo to self if Echo mode
        if (txCh.WorkMode == ChannelWorkMode.Echo)
        {
            if (isFd)
                EnqueueFd(txCh, can_id, lenOrDlc, (byte)(fdFlags | TX_ECHO_FLAG), data);
            else
                EnqueueClassic(txCh, can_id, lenOrDlc, data);
            EnqueueMerge(dev, (byte)txCh.Index, isFd, can_id, lenOrDlc, (byte)(fdFlags | TX_ECHO_FLAG), data);
        }

        // linked peer
        if (IsLinkedPair(dev.Type, dev.Index, txCh.Index))
        {
            int peerIdx = LinkedPeerIndex(txCh.Index);
            if (peerIdx >= 0 && dev.Channels.TryGetValue(peerIdx, out var rxCh) && rxCh.Started)
            {
                // protocol/bitrate gate
                if (!BitrateMatches(rxCh, txCh, isFd)) return;
                if (isFd && !rxCh.FdEnabled && !dev.MergeReceive) return; // no FD if not enabled
                if (!AcceptByFilter(rxCh, can_id)) return;

                if (isFd)
                    EnqueueFd(rxCh, can_id, lenOrDlc, fdFlags, data);
                else
                    EnqueueClassic(rxCh, can_id, lenOrDlc, data);
                EnqueueMerge(dev, (byte)rxCh.Index, isFd, can_id, lenOrDlc, fdFlags, data);
            }
        }
    }

    private static bool TryGetDevice(IntPtr devHandle, out Device dev)
    {
        lock (World.Gate) return World.Devices.TryGetValue(devHandle, out dev!);
    }

    private static bool TryGetChannel(IntPtr chHandle, out Channel ch)
    {
        lock (World.Gate) return World.Channels.TryGetValue(chHandle, out ch!);
    }

    // ---- API: Device ----
    public static IntPtr ZCAN_OpenDevice(uint device_type, uint device_index, uint reserved)
    {
        _ = reserved;
        lock (World.Gate)
        {
            // reuse existing to mimic multiplexer behaviour
            foreach (var d in World.Devices.Values)
            {
                if (d.Type == device_type && d.Index == device_index)
                    return d.Handle;
            }

            var dev = new Device
            {
                Type = device_type,
                Index = device_index,
                Handle = World.NextDev
            };
            World.NextDev = (IntPtr)((long)World.NextDev + 1);
            World.Devices[dev.Handle] = dev;
            for(int i = 0 ; i<2;i++)
            {
                dev.Channels[i] = new Channel
                {
                    Dev = dev,
                    Index = i,
                    Handle = World.NextCh,
                    Started = false,
                };
                World.NextCh = (IntPtr)((long)World.NextCh + 1);
                World.Channels[dev.Channels[i].Handle] = dev.Channels[i];
            }
            return dev.Handle;
        }
    }

    public static uint ZCAN_CloseDevice(IntPtr device_handle)
    {
        lock (World.Gate)
        {
            if (World.Devices.Remove(device_handle))
            {
                // also drop channels
                var drop = World.Channels.Where(kv => kv.Value.Dev.Handle == device_handle).Select(kv => kv.Key).ToArray();
                foreach (var ch in drop) World.Channels.Remove(ch);
            }
        }
        return OK;
    }

    // ---- API: Channel ----
    public static ZlgChannelHandle ZCAN_InitCAN(ZlgDeviceHandle device_handle, uint can_index, ref ZCAN_CHANNEL_INIT_CONFIG pInitConfig)
    {
        if (device_handle == null || device_handle.IsInvalid) return new ZlgChannelHandle();
        if (!TryGetDevice(device_handle.DangerousGetHandle(), out var dev)) return new ZlgChannelHandle();

        var ch = World.Devices[dev.Handle].Channels[(int)can_index];

        // Apply initial config
        ch.FdEnabled = pInitConfig.can_type != 0; // 0=Classic, 1=FD (adapter convention)

        // possible initial mask filter
        if (pInitConfig.config.can.acc_mask != 0xffffffff || pInitConfig.config.can.acc_code != 0)
        {
            ch.HasMaskFilter = true;
            ch.MaskAccCode = pInitConfig.config.can.acc_code;
            ch.MaskAccMask = pInitConfig.config.can.acc_mask;
            ch.MaskIdType = (CanFilterIDType)pInitConfig.config.can.filter;
        }

        var ret = new ZlgChannelHandle();
        // set handle pointer (channel id); Device pointer will be set by caller via SetDevice
        typeof(SafeHandle).GetField("handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(ret, ch.Handle);
        return ret;
    }

    public static uint ZCAN_StartCAN(ZlgChannelHandle chn_handle)
    {
        if (!TryGetChannel(chn_handle.DangerousGetHandle(), out var ch)) return 0;
        ch.Started = true;
        return OK;
    }

    public static uint ZCAN_ResetCAN(ZlgChannelHandle chn_handle)
    {
        if (!TryGetChannel(chn_handle.DangerousGetHandle(), out var ch)) return 0;
        while (ch.RxClassic.TryDequeue(out _)) { }
        while (ch.RxFd.TryDequeue(out _)) { }
        return OK;
    }

    public static uint ZCAN_ClearBuffer(ZlgChannelHandle chn_handle) => ZCAN_ResetCAN(chn_handle);

    public static uint ZCAN_GetReceiveNum(ZlgChannelHandle channel_handle, byte type)
    {
        if (!TryGetChannel(channel_handle.DangerousGetHandle(), out var ch)) return 0;
        if (type == 0) return (uint)ch.RxClassic.Count;
        if (type == 1) return (uint)ch.RxFd.Count;
        return 0;
    }

    // ---- API: Transmit (classic) ----
    public static unsafe uint ZCAN_Transmit(ZlgChannelHandle channel_handle, ZCAN_Transmit_Data* pTransmit, uint len)
    {
        if (!TryGetChannel(channel_handle.DangerousGetHandle(), out var ch)) return 0;
        var dev = ch.Dev;
        uint sent = 0;
        for (int i = 0; i < len; i++)
        {
            var td = pTransmit + i;
            var can_id = td->frame.can_id;
            int dlc = td->frame.can_dlc;
            var buf = new byte[dlc];
            for (int b = 0; b < dlc; b++) buf[b] = td->frame.data[b];
            RouteTx(dev, ch, false, can_id, (byte)dlc, 0, buf);
            sent++;
        }
        return sent;
    }

    // ---- API: TransmitFD ----
    public static unsafe uint ZCAN_TransmitFD(ZlgChannelHandle channel_handle, ZCAN_TransmitFD_Data* pTransmit, uint len)
    {
        if (!TryGetChannel(channel_handle.DangerousGetHandle(), out var ch)) return 0;
        var dev = ch.Dev;
        uint sent = 0;
        for (int i = 0; i < len; i++)
        {
            var td = pTransmit + i;
            var can_id = td->frame.can_id;
            int l = td->frame.len;
            if (l < 0) l = 0; if (l > 64) l = 64;
            var buf = new byte[l];
            for (int b = 0; b < l; b++) buf[b] = td->frame.data[b];
            RouteTx(dev, ch, true, can_id, (byte)l, td->frame.flags, buf);
            sent++;
        }
        return sent;
    }

    // ---- API: TransmitData (merge) ----
    public static unsafe uint ZCAN_TransmitData(ZlgDeviceHandle device_handle, ZCANDataObj* pTransmit, uint len)
        => ZCAN_TransmitData(device_handle?.DangerousGetHandle() ?? IntPtr.Zero, pTransmit, len);

    public static unsafe uint ZCAN_TransmitData(IntPtr device_handle, ZCANDataObj* pTransmit, uint len)
    {
        if (!TryGetDevice(device_handle, out var dev)) return 0;
        uint sent = 0;
        for (int i = 0; i < len; i++)
        {
            var obj = pTransmit + i;
            if (obj->dataType != 1) continue; // only CAN
            bool isFd = obj->data.fdData.frameType == 1;
            byte chIndex = obj->chnl;
            if (!dev.Channels.TryGetValue(chIndex, out var ch)) continue;
            var can_id = obj->data.fdData.frame.can_id;
            int l = obj->data.fdData.frame.len;
            if (isFd) { if (l < 0) l = 0; if (l > 64) l = 64; }
            else { if (l < 0) l = 0; if (l > 8) l = 8; }
            var buf = new byte[l];
            for (int b = 0; b < l; b++) buf[b] = obj->data.fdData.frame.data[b];
            RouteTx(dev, ch, isFd, can_id, (byte)l, obj->data.fdData.frame.flags, buf);
            sent++;
        }
        return sent;
    }

    // ---- API: Receive (classic) ----
    public static unsafe uint ZCAN_Receive(ZlgChannelHandle channel_handle, [In, Out] ZCAN_Receive_Data[] pReceive, uint len, int wait_time = -1)
    {
        if (pReceive == null || pReceive.Length == 0) return 0;
        if (!TryGetChannel(channel_handle.DangerousGetHandle(), out var ch)) return 0;

        uint count = 0;
        var start = Environment.TickCount;
        DequeueQueue();
        while (count < len)
        {
            var remaining = wait_time + start - Environment.TickCount;
            if (remaining <= 0 && wait_time != -1) break;
            ch.RxEvtClassic.WaitOne(TimeSpan.FromMilliseconds(Math.Min(remaining, 10)));
            DequeueQueue();
        }
        return count;

        void DequeueQueue()
        {
            while (count < len && ch.RxClassic.TryDequeue(out var item))
            {
                pReceive[count++] = item;
            }
        }
    }

    // ---- API: ReceiveFD ----
    public static unsafe uint ZCAN_ReceiveFD(ZlgChannelHandle channel_handle, [In, Out] ZCAN_ReceiveFD_Data[] pReceive, uint len, int wait_time = -1)
    {
        if (pReceive == null || pReceive.Length == 0) return 0;
        if (!TryGetChannel(channel_handle.DangerousGetHandle(), out var ch)) return 0;

        uint count = 0;
        var start = Environment.TickCount;
        DequeueQueue();
        while (count < len)
        {
            var remaining = wait_time + start - Environment.TickCount;
            if (remaining <= 0 && wait_time != -1) break;
            ch.RxEvtFd.WaitOne(TimeSpan.FromMilliseconds(Math.Min(remaining, 10)));
            DequeueQueue();
        }
        return count;

        void DequeueQueue()
        {
            while (count < len && ch.RxFd.TryDequeue(out var item))
            {
                pReceive[count++] = item;
            }
        }
    }

    // ---- API: ReceiveData (merge) ----
    public static unsafe uint ZCAN_ReceiveData(ZlgDeviceHandle device_handle, [In, Out] ZCANDataObj[] pReceive, uint len, int wait_time)
        => ZCAN_ReceiveData(device_handle?.DangerousGetHandle() ?? IntPtr.Zero, pReceive, len, wait_time);

    public static unsafe uint ZCAN_ReceiveData(IntPtr device_handle, [In, Out] ZCANDataObj[] pReceive, uint len, int wait_time)
    {
        if (pReceive == null || pReceive.Length == 0) return 0;
        if (!TryGetDevice(device_handle, out var dev)) return 0;
        uint count = 0;
        var start = Environment.TickCount;
        DequeueQueue();
        while (count < len)
        {
            var remaining = wait_time + start - Environment.TickCount;
            if (remaining <= 0 && wait_time != -1) break;
            dev.MergeRxEvt.WaitOne(TimeSpan.FromMilliseconds(Math.Min(remaining, 10)));
            DequeueQueue();
        }
        return count;

        void DequeueQueue()
        {
            while (count < len && dev.MergeRx.TryDequeue(out var item))
            {
                pReceive[count++] = item;
            }
        }
    }

    // ---- API: SetValue/GetValue ----
    public static uint ZCAN_SetValue(ZlgDeviceHandle device_handle, string path, string value)
        => ZCAN_SetValue(device_handle?.DangerousGetHandle() ?? IntPtr.Zero, path, value);

    public static uint ZCAN_SetValue(IntPtr device_handle, string path, string value)
    {
        if (!TryGetDevice(device_handle, out var dev)) return 0;
        if (string.IsNullOrWhiteSpace(path)) return 0;
        var parts = path.Split(new[]{'/'}, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 0;

        if (!int.TryParse(parts[0], out int chIdx)) return 0;
        if (!dev.Channels.TryGetValue(chIdx, out var ch)) return 0;

        string key = parts.Length >= 2 ? parts[1] : string.Empty;
        switch (key)
        {
            case "baud_rate":
                _ = uint.TryParse(value, out ch.ClassicBaud);
                break;
            case "canfd_abit_baud_rate":
                _ = uint.TryParse(value, out ch.ArbBaud);
                break;
            case "canfd_dbit_baud_rate":
                _ = uint.TryParse(value, out ch.DataBaud);
                break;
            case "work_mode":
                if (int.TryParse(value, out var wm)) ch.WorkMode = (ChannelWorkMode)wm;
                break;
            case "acc_code":
                ch.HasMaskFilter = true;
                ch.RangeRules.Clear();
                ch.MaskAccCode = ParseHexUInt(value);
                break;
            case "acc_mask":
                ch.HasMaskFilter = true;
                ch.RangeRules.Clear();
                ch.MaskAccMask = ParseHexUInt(value);
                break;
            case "filter_mode":
                if (int.TryParse(value, out var m)) { ch.PendingHasIdType = true; ch.PendingIdType = (CanFilterIDType)m; }
                break;
            case "filter_start":
                ch.PendingHasStart = true;
                ch.PendingStart = ParseHexUInt(value);
                break;
            case "filter_end":
                {
                    var end = ParseHexUInt(value);
                    if (ch.PendingHasStart && ch.PendingHasIdType)
                    {
                        var rr = new RangeRule
                        {
                            IdType = ch.PendingIdType,
                            Start = ch.PendingStart,
                            End = end
                        };
                        ch.PendingRangeRules.Add(rr);
                        ch.PendingHasStart = false; // consume current start
                    }
                    else
                    {
                        return 0;
                    }
                }
                break;
            case "filter_ack":
                // Commit all pending range records
                ch.RangeRules.AddRange(ch.PendingRangeRules);
                ch.PendingRangeRules.Clear();
                ch.HasMaskFilter = false; // switch to range mode
                break;
            case "set_bus_usage_period":
                _ = uint.TryParse(value, out dev.BusUsagePeriodMs);
                break;
            case "set_bus_usage_enable":
                dev.BusUsageEnabled = value == "1";
                break;
            case "set_tx_retry_policy":
                // ignore
                break;
            case "initenal_resistance":
                // ignore
                break;
            case "set_device_recv_merge":
                dev.MergeReceive = value == "1";
                break;
        }
        return OK;
    }

    public static uint ZCAN_SetValue(ZlgDeviceHandle device_handle, string path, IntPtr value) => OK;
    public static uint ZCAN_SetValue(IntPtr device_handle, string path, IntPtr value) => OK;

    public static IntPtr ZCAN_GetValue(IntPtr device_handle, string path)
    {
        if (!TryGetDevice(device_handle, out var dev)) return IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(path)) return IntPtr.Zero;
        var parts = path.Split(new[]{'/'}, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[1] == "get_bus_usage")
        {
            // Return a temporary unmanaged buffer with BusUsage struct
            var bu = new BusUsage
            {
                nTimeStampBegin = 0,
                nTimeStampEnd = NowUs(),
                nChnl = (byte)int.Parse(parts[0]),
                nReserved = 0,
                nBusUsage = (ushort)dev.BusUsagePermille,
                nFrameCount = 0,
            };
            int size = Marshal.SizeOf<BusUsage>();
            IntPtr mem = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(bu, mem, false);
            return mem;
        }
        return IntPtr.Zero;
    }

    public static IntPtr ZCAN_GetValue(ZlgDeviceHandle device_handle, string path)
        => ZCAN_GetValue(device_handle?.DangerousGetHandle() ?? IntPtr.Zero, path);

    // ---- API: Errors/Status ----
    public static uint ZCAN_IsDeviceOnLine(ZlgDeviceHandle device_handle) => device_handle is { IsInvalid: false } ? OK : 0u;

    public static uint ZCAN_ReadChannelErrInfo(ZlgChannelHandle channel_handle, out ZCAN_CHANNEL_ERROR_INFO pErrInfo)
    {
        pErrInfo = new ZCAN_CHANNEL_ERROR_INFO
        {
            error_code = 0,
            passive_ErrData = new byte[3],
            arLost_ErrData = 0
        };
        if (TryGetChannel(channel_handle.DangerousGetHandle(), out var ch))
        {
            pErrInfo.passive_ErrData[1] = ch.Tec;
            pErrInfo.passive_ErrData[2] = ch.Rec;
        }
        return OK;
    }

    public static uint ZCAN_ReadChannelStatus(ZlgChannelHandle channel_handle, out ZCAN_CHANNEL_STATUS pCANStatus)
    {
        pCANStatus = new ZCAN_CHANNEL_STATUS();
        return OK;
    }

    // ---- Utility ----
    private static uint ParseHexUInt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var v)) return v;
            return 0;
        }
        _ = uint.TryParse(s, out var dec);
        return dec;
    }

    // ---- Structs (mirror a subset so consumers compile) ----
    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_CHANNEL_INIT_CONFIG
    {
        public uint can_type; // 0=CAN, 1=FD
        public _ZCAN_CHANNEL_INIT_CONFIG config;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct _ZCAN_CHANNEL_INIT_CONFIG
    {
        public _ZCAN_CHANNEL_CAN_INIT_CONFIG can;
        public _ZCAN_CHANNEL_CANFD_INIT_CONFIG canfd;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct _ZCAN_CHANNEL_CAN_INIT_CONFIG
    {
        public uint acc_code;
        public uint acc_mask;
        public uint reserved;
        public byte filter; // 0=std,1=ext
        public byte timing0;
        public byte timing1;
        public byte mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct _ZCAN_CHANNEL_CANFD_INIT_CONFIG
    {
        public uint acc_code;
        public uint acc_mask;
        public uint abit_timing;
        public uint dbit_timing;
        public uint brp;
        public byte filter;
        public byte mode;
        public ushort pad;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct can_frame
    {
        public uint can_id;
        public byte can_dlc;
        public byte __pad;
        public byte __res0;
        public byte __res1;
        public fixed byte data[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct canfd_frame
    {
        public uint can_id;
        public byte len;
        public byte flags;
        public byte __res0;
        public byte __res1;
        public fixed byte data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_Transmit_Data
    {
        public can_frame frame;
        public uint transmit_type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_Receive_Data
    {
        public can_frame frame;
        public UInt64 timestamp; // us
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_TransmitFD_Data
    {
        public canfd_frame frame;
        public uint transmit_type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_ReceiveFD_Data
    {
        public canfd_frame frame;
        public UInt64 timestamp; // us
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_AUTO_TRANSMIT_OBJ
    {
        public ushort enable;     // 0-disable, 1-enable
        public ushort index;      // periodic index
        public uint interval;     // ms
        public ZCAN_Transmit_Data obj;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCANFD_AUTO_TRANSMIT_OBJ
    {
        public ushort enable;     // 0-disable, 1-enable
        public ushort index;      // periodic index
        public uint interval;     // ms
        public ZCAN_TransmitFD_Data obj;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZCANCANFDData
    {
        public UInt64 timeStamp;
        public UInt32 flag;
        public fixed byte extraData[4];
        public canfd_frame frame;
        public uint frameType
        {
            get => (flag & 0x03u);
            set => flag = (flag & ~0x03u) | (value & 0x03u);
        }
        public uint transmitType
        {
            get => (flag >> 4) & 0x0Fu;
            set => flag = (flag & ~0xF0u) | ((value & 0x0Fu) << 4);
        }
        public bool txEchoRequest
        {
            get => ((flag >> 8) & 1u) != 0;
            set => flag = value ? (flag | (1u << 8)) : (flag & ~(1u << 8));
        }
        public bool txEchoed
        {
            get => ((flag >> 9) & 1u) != 0;
            set => flag = value ? (flag | (1u << 9)) : (flag & ~(1u << 9));
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZCANErrorData
    {
        public ulong timeStamp;
        public byte errType;
        public byte errSubType;
        public byte nodeState;
        public byte rxErrCount;
        public byte txErrCount;
        public byte errData;
        public fixed byte reserved[2];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZCANDataUnion
    {
        public ZCANCANFDData fdData;
        public ZCANErrorData errData;
        public fixed byte data[92];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ZCANDataObj
    {
        public byte dataType; // 1=CAN/CANFD, 4=LIN, 5=BusUsage
        public byte chnl;
        public UInt16 flag;
        public fixed byte extraData[4];
        public ZCANDataUnion data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_CHANNEL_ERROR_INFO
    {
        public uint error_code;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] passive_ErrData;
        public byte arLost_ErrData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ZCAN_CHANNEL_STATUS
    {
        public byte errInterrupt;
        public byte regMode;
        public byte regStatus;
        public byte regALCapture;
        public byte regECCapture;
        public byte regEWLimit;
        public byte regRECounter;
        public byte regTECounter;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BusUsage
    {
        public UInt64 nTimeStampBegin;
        public UInt64 nTimeStampEnd;
        public byte nChnl;
        public byte nReserved;
        public ushort nBusUsage; // *100
        public uint nFrameCount;
    }
}

#endif
