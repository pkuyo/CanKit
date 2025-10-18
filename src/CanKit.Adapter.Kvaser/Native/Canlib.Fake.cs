// FAKE Kvaser CANlib backend for unit tests
#if FAKE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
// @formatter:off
#nullable disable
#pragma warning disable IDE0055
namespace CanKit.Adapter.Kvaser.Native;

public static class Canlib
{
    // ---- Public API surface (mirrors real Canlib.cs) ----
    public enum canStatus : int
    {
        canOK = 0,
        canERR_PARAM = -1,
        canERR_NOMSG = -2,
        canERR_NOTFOUND = -3,
        canERR_NOMEM = -4,
        canERR_NOCHANNELS = -5,
        canERR_INTERRUPTED = -6,
        canERR_TIMEOUT = -7,
        canERR_NOTINITIALIZED = -8,
        canERR_NOHANDLES = -9,
        canERR_INVHANDLE = -10,
        canERR_DRIVER = -12,
        canERR_TXBUFOFL = -13,
        canERR_HARDWARE = -15,
        canERR_DYNALOAD = -16,
        canERR_DYNALIB = -17,
        canERR_DYNAINIT = -18,
        canERR_NOT_SUPPORTED = -19,
        canERR_DRIVERLOAD = -23,
        canERR_DRIVERFAILED = -24,
        canERR_NOCARD = -26,
        canERR_REGISTRY = -28,
        canERR_INTERNAL = -30,
        canERR_NO_ACCESS = -31,
        canERR_NOT_IMPLEMENTED = -32,
    }

    // Open channel flags (subset)
    public const int canOPEN_ACCEPT_VIRTUAL = 0x0020;
    public const int canOPEN_CAN_FD = 0x0400;
    // Message flags
    public const int canMSG_RTR = 0x0001;
    public const int canMSG_STD = 0x0002;
    public const int canMSG_EXT = 0x0004;
    public const int canMSG_ERROR_FRAME = 0x0020;
    public const int canMSG_SINGLE_SHOT = 0x1000000;
    public const int canMSG_TXNACK = 0x2000000;
    public const int canMSG_ABL = 0x4000000;

    // CAN FD message flags
    public const int canFDMSG_FDF = 0x010000;
    public const int canFDMSG_BRS = 0x020000;
    public const int canFDMSG_ESI = 0x040000;

    // Notification flags (subset)
    public const int canNOTIFY_RX = 0x0001;
    public const int canNOTIFY_ERROR = 0x0004;

    // Bus status flags
    public const int canSTAT_ERROR_PASSIVE = 0x00000001;
    public const int canSTAT_BUS_OFF = 0x00000002;
    public const int canSTAT_ERROR_WARNING = 0x00000004;
    public const int canSTAT_ERROR_ACTIVE = 0x00000008;

    // IOCTL codes (subset used)
    public const uint canIOCTL_FLUSH_RX_BUFFER = 10;
    public const uint canIOCTL_FLUSH_TX_BUFFER = 11;
    public const uint canIOCTL_SET_TIMER_SCALE = 6;
    public const uint canIOCTL_SET_RX_QUEUE_SIZE = 27;

    // Predefined classic bitrates
    public const int canBITRATE_1M = -1;
    public const int canBITRATE_500K = -2;
    public const int canBITRATE_250K = -3;
    public const int canBITRATE_125K = -4;
    public const int canBITRATE_100K = -5;
    public const int canBITRATE_62K = -6;
    public const int canBITRATE_50K = -7;
    public const int canBITRATE_83K = -8;
    public const int canBITRATE_10K = -9;

    // Predefined FD bitrates (bps@sample-point)
    public const int canFD_BITRATE_500K_80P = -1000;
    public const int canFD_BITRATE_1M_80P = -1001;
    public const int canFD_BITRATE_2M_80P = -1002;
    public const int canFD_BITRATE_4M_80P = -1003;
    public const int canFD_BITRATE_8M_60P = -1004;
    public const int canFD_BITRATE_8M_80P = -1005;
    public const int canFD_BITRATE_8M_70P = -1006;
    public const int canFD_BITRATE_2M_60P = -1007;

    // Channel data item ids (subset)
    public const int canCHANNELDATA_CHANNEL_CAP = 1;
    public const int canCHANNELDATA_CARD_UPC_NO = 11;
    public const int canCHANNELDATA_CARD_SERIAL_NO = 7;
    public const int canCHANNELDATA_CHANNEL_NAME = 13;
    public const int canCHANNELDATA_CHANNEL_CAP_EX = 47;

    // Channel capability bits (subset)
    public const uint canCHANNEL_CAP_CAN_FD = 0x00080000;
    public const uint canCHANNEL_CAP_CAN_FD_NONISO = 0x00100000;
    public const uint canCHANNEL_CAP_SILENT_MODE = 0x00200000;
    public const uint canCHANNEL_CAP_ERROR_COUNTERS = 0x00000004;
    public const uint canCHANNEL_CAP_BUS_STATISTICS = 0x00000002;

    // Object buffer types
    public enum canObjBufType : int
    {
        AUTO_RESPONSE = 0x01,
        PERIODIC_TX = 0x02
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void kvCallbackDelegate(int hnd, IntPtr context, int notifyEvent);

    // ---- Internal fake world ----
    private sealed class Frame
    {
        public int Id;
        public byte[] Data = Array.Empty<byte>();
        public int Dlc;
        public int Flags;
        public uint Time; // arbitrary unit; KvaserBus scales with TimerScale
    }

    private sealed class FilterRule
    {
        public uint Code;
        public uint Mask;
        public bool Extended;
        public bool Match(int id, bool ext)
        {
            if (ext != Extended) return false;
            uint uid = (uint)id;
            return (uid & Mask) == (Code & Mask);
        }
    }

    private sealed class PeriodicBuf
    {
        public int Index;
        public int Id;
        public byte[] Data = Array.Empty<byte>();
        public int Dlc;
        public uint Flags;
        public int PeriodUs = 1000;
        public Timer Timer;
    }

    private sealed class Handle
    {
        public int HandleId;
        public int Channel; // 0,1,2
        public bool BusOn;
        public int TimerScaleUs = 1000; // default
        public int RxQueueSize = 0; // 0 => unlimited
        public kvCallbackDelegate Callback;
        public IntPtr CallbackCtx = IntPtr.Zero;
        public uint CallbackMask = 0;
        public readonly ConcurrentQueue<Frame> Rx = new();
        public readonly List<FilterRule> Filters = new();
        public readonly Dictionary<int, PeriodicBuf> Periodics = new();
    }

    private static class World
    {
        public static readonly object Gate = new();
        public static int NextHandle = 1000;
        public static readonly Dictionary<int, Handle> Handles = new();
        public static readonly int[] Channels = new[] { 0, 1, 2 };
    }

    private static bool TryGetHandle(int hnd, out Handle h)
    {
        lock (World.Gate)
        {
            return World.Handles.TryGetValue(hnd, out h!);
        }
    }

    private static void Notify(Handle h)
    {
        var cb = h.Callback;
        if (cb != null && (h.CallbackMask & canNOTIFY_RX) != 0)
        {
            try { cb(h.HandleId, h.CallbackCtx, canNOTIFY_RX); } catch { }
        }
    }

    private static void EnqueueToReceivers(Handle sender, Frame frame)
    {
        // Deliver to all open handles on channels 0/1/2 except sender, if BusOn and matches filter.
        List<Handle> targets;
        lock (World.Gate)
        {
            targets = World.Handles.Values.Where(x => x.HandleId != sender.HandleId && World.Channels.Contains(x.Channel) && x.BusOn).ToList();
        }

        foreach (var t in targets)
        {
            bool accepted = true;
            if (t.Filters.Count > 0)
            {
                var filter = t.Filters.FirstOrDefault(i => i.Extended == ((frame.Flags & canMSG_EXT) != 0));
                if(filter is not null)
                    accepted = ((filter.Code ^ (frame.Id & (filter.Extended ? 0x1FFF_FFFF : 0x7FF))) & filter.Mask) == 0;
            }

            if (!accepted) continue;

            // Copy frame as value
            var copy = new Frame
            {
                Id = frame.Id,
                Data = frame.Data.ToArray(),
                Dlc = frame.Dlc,
                Flags = frame.Flags,
                Time = frame.Time
            };
            t.Rx.Enqueue(copy);
            Notify(t);
        }
    }

    // ---- API implementations ----
    public static void canInitializeLibrary() { /* no-op */ }

    public static canStatus canUnloadLibrary() => canStatus.canOK;

    public static int canOpenChannel(int channel, int flags)
    {
        _ = flags; // unused in fake
        if (channel < 0 || channel > 2) return (int)canStatus.canERR_NOCHANNELS;
        var h = new Handle { Channel = channel };
        lock (World.Gate)
        {
            h.HandleId = World.NextHandle++;
            World.Handles[h.HandleId] = h;
            return h.HandleId;
        }
    }

    public static canStatus canClose(int hnd)
    {
        Handle h;
        lock (World.Gate)
        {
            if (!World.Handles.TryGetValue(hnd, out h)) return canStatus.canERR_INVHANDLE;
            World.Handles.Remove(hnd);
        }
        try
        {
            // Stop all periodic timers
            foreach (var p in h!.Periodics.Values) { try { p.Timer?.Dispose(); } catch { } }
            h.Periodics.Clear();
        }
        catch { }
        return canStatus.canOK;
    }

    public static canStatus canBusOn(int hnd)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        h.BusOn = true;
        return canStatus.canOK;
    }

    public static canStatus canBusOff(int hnd)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        h.BusOn = false;
        // optional: flush Rx
        while (h.Rx.TryDequeue(out _)) { }
        return canStatus.canOK;
    }

    public static canStatus canReadStatus(int hnd, out int status)
    {
        status = 0;
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        status |= h.BusOn ? canSTAT_ERROR_ACTIVE : canSTAT_BUS_OFF;
        return canStatus.canOK;
    }

    public static canStatus canSetBusParams(int hnd, int bitrate, int tseg1, int tseg2, int sjw, int noSamp, int syncmode)
    {
        _ = hnd; _ = bitrate; _ = tseg1; _ = tseg2; _ = sjw; _ = noSamp; _ = syncmode;
        return canStatus.canOK;
    }

    public static canStatus canSetBusParamsFd(int hnd, int data_bitrate, int tseg1, int tseg2, int sjw)
    {
        _ = hnd; _ = data_bitrate; _ = tseg1; _ = tseg2; _ = sjw;
        return canStatus.canOK;
    }

    public static canStatus canReadErrorCounters(int hnd, out uint txErr, out uint rxErr, out uint ovErr)
    {
        _ = hnd; txErr = 0; rxErr = 0; ovErr = 0; return canStatus.canOK;
    }

    public static unsafe canStatus canWrite(int hnd, int id, byte* msg, uint dlc, uint flag)
    {
        if (!TryGetHandle(hnd, out var h) || !h.BusOn) return canStatus.canERR_INVHANDLE;

        byte[] data = Array.Empty<byte>();
        if (msg != null && dlc > 0)
        {
            var len = (int)dlc;
            data = new byte[len];
            for (int i = 0; i < len; i++) data[i] = msg[i];
        }

        var frame = new Frame
        {
            Id = id,
            Data = data,
            Dlc = (int)dlc,
            Flags = (int)flag,
            Time = 0 // simple timestamp; KvaserBus scales it
        };

        EnqueueToReceivers(h, frame);
        return canStatus.canOK;
    }

    public static canStatus canRead(int hnd, out int id, byte[] msg, out int dlc, out int flag, out uint time)
    {
        id = 0; dlc = 0; flag = 0; time = 0;
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        if (h.Rx.TryDequeue(out var f))
        {
            id = f.Id;
            dlc = Math.Min(msg.Length, f.Dlc);
            if (dlc > 0) Array.Copy(f.Data, 0, msg, 0, dlc);
            flag = f.Flags;
            time = f.Time;
            return canStatus.canOK;
        }
        return canStatus.canERR_NOMSG;
    }

    // Bus usage/statistics
    [StructLayout(LayoutKind.Sequential)]
    public struct canBusStatistics
    {
        public uint stdData;
        public uint stdRemote;
        public uint extData;
        public uint extRemote;
        public uint errFrame;
        public uint busLoad;   // 0-10000 => 0.00% - 100.00%
        public uint overruns;
    }

    public static canStatus canRequestBusStatistics(int hnd)
    { _ = hnd; return canStatus.canOK; }

    public static canStatus canGetBusStatistics(int hnd, out canBusStatistics stat, UIntPtr bufsiz)
    {
        _ = hnd; _ = bufsiz;
        stat = new canBusStatistics { busLoad = 0 };
        return canStatus.canOK;
    }

    public static canStatus canGetErrorText(canStatus err, StringBuilder buf, uint bufsiz)
    {
        string s = err.ToString();
        var msg = Encoding.ASCII.GetBytes(s + "\0");
        int n = (int)Math.Min((uint)msg.Length, bufsiz);
        buf.Clear();
        buf.Append(s);
        return canStatus.canOK;
    }

    // IOCTLs
    public static canStatus canIoCtl(int hnd, uint func, IntPtr buf, uint buflen)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        if (func == canIOCTL_FLUSH_RX_BUFFER)
        {
            while (h.Rx.TryDequeue(out _)) { }
            return canStatus.canOK;
        }
        if (func == canIOCTL_FLUSH_TX_BUFFER)
        {
            return canStatus.canOK;
        }
        return canStatus.canOK;
    }

    public static canStatus canIoCtl(int hnd, uint func, ref int value, uint buflen)
    {
        _ = buflen;
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        if (func == canIOCTL_SET_RX_QUEUE_SIZE)
        {
            h.RxQueueSize = value; return canStatus.canOK;
        }
        if (func == canIOCTL_SET_TIMER_SCALE)
        {
            h.TimerScaleUs = value; return canStatus.canOK;
        }
        return canStatus.canOK;
    }

    public static canStatus canIoCtl(int hnd, uint func, ref uint value, uint buflen)
    {
        _ = hnd; _ = func; _ = value; _ = buflen; return canStatus.canOK;
    }

    public static canStatus canIoCtl(int hnd, uint func, byte[] buffer, uint buflen)
    {
        _ = hnd; _ = func; _ = buffer; _ = buflen; return canStatus.canOK;
    }

    public static canStatus canIoCtl(int hnd, uint func, StringBuilder sb, uint buflen)
    {
        _ = hnd; _ = func; _ = sb; _ = buflen; return canStatus.canOK;
    }

    public static canStatus canAccept(int hnd, int envelope, uint flag)
    {
        _ = hnd; _ = envelope; _ = flag; return canStatus.canERR_NOT_IMPLEMENTED;
    }

    public static canStatus canSetAcceptanceFilter(int hnd, uint code, uint mask, int is_extended)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        var rule = new FilterRule { Code = code, Mask = mask, Extended = is_extended != 0 };
        lock (h.Filters)
        {
            h.Filters.RemoveAll(i => i.Extended == (is_extended != 0));
            h.Filters.Add(rule);
        }
        return canStatus.canOK;
    }

    // Object buffers for periodic TX
    public static canStatus canObjBufAllocate(int hnd, int type)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        if (type != (int)canObjBufType.PERIODIC_TX) return canStatus.canERR_NOT_SUPPORTED;
        int idx;
        lock (h.Periodics)
        {
            idx = (h.Periodics.Count == 0) ? 1 : (h.Periodics.Keys.Max() + 1);
            h.Periodics[idx] = new PeriodicBuf { Index = idx };
        }
        return (canStatus)idx; // Kvaser returns idx as status
    }

    public static canStatus canObjBufFree(int hnd, int idx)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        lock (h.Periodics)
        {
            if (h.Periodics.TryGetValue(idx, out var p)) { try { p.Timer?.Dispose(); } catch { } h.Periodics.Remove(idx); }
        }
        return canStatus.canOK;
    }

    public static canStatus canObjBufEnable(int hnd, int idx)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        PeriodicBuf p;
        lock (h.Periodics) { if (!h.Periodics.TryGetValue(idx, out p!)) return canStatus.canERR_PARAM; }
        // Start timer
        int due = Math.Max(1, p.PeriodUs / 1000);
        p.Timer?.Dispose();
        p.Timer = new Timer(_ =>
        {
            // Construct frame and send
            var f = new Frame { Id = p.Id, Data = p.Data.ToArray(), Dlc = p.Dlc, Flags = (int)p.Flags, Time = 0 };
            EnqueueToReceivers(h, f);
        }, null, due, due);
        return canStatus.canOK;
    }

    public static canStatus canObjBufDisable(int hnd, int idx)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        lock (h.Periodics)
        {
            if (h.Periodics.TryGetValue(idx, out var p) && p.Timer != null)
            {
                try { p.Timer.Dispose(); } catch { }
                p.Timer = null;
            }
        }
        return canStatus.canOK;
    }

    public static canStatus canObjBufWrite(int hnd, int idx, int id, byte[] msg, uint dlc, uint flags)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        lock (h.Periodics)
        {
            if (!h.Periodics.TryGetValue(idx, out var p)) return canStatus.canERR_PARAM;
            p.Id = id;
            p.Dlc = (int)dlc;
            p.Flags = flags;
            p.Data = msg?.Take((int)dlc).ToArray() ?? Array.Empty<byte>();
        }
        return canStatus.canOK;
    }

    public static canStatus canObjBufSetPeriod(int hnd, int idx, uint periodUs)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        lock (h.Periodics)
        {
            if (!h.Periodics.TryGetValue(idx, out var p)) return canStatus.canERR_PARAM;
            p.PeriodUs = (int)Math.Max(1, periodUs);
        }
        return canStatus.canOK;
    }

    public static canStatus kvSetNotifyCallback(int hnd, kvCallbackDelegate callback, IntPtr context, uint notifyFlags)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        h.Callback = callback;
        h.CallbackCtx = context;
        h.CallbackMask = notifyFlags;
        return canStatus.canOK;
    }

    // Channel enumeration/data
    public static canStatus canGetNumberOfChannels(out int channelCount)
    {
        channelCount = 3; return canStatus.canOK;
    }

    public static canStatus canGetChannelData_UInt32(int channel, int item, out uint value, UIntPtr bufsize)
    {
        _ = bufsize;
        value = 0;
        if (channel < 0 || channel > 2) return canStatus.canERR_PARAM;
        if (item == canCHANNELDATA_CHANNEL_CAP)
        {
            value = canCHANNEL_CAP_CAN_FD | canCHANNEL_CAP_CAN_FD_NONISO | canCHANNEL_CAP_SILENT_MODE | canCHANNEL_CAP_ERROR_COUNTERS | canCHANNEL_CAP_BUS_STATISTICS;
            return canStatus.canOK;
        }
        return canStatus.canERR_NOT_IMPLEMENTED;
    }

    public static canStatus canGetChannelData_UInt32Array(int channel, int item, uint[] buffer, UIntPtr bufsize)
    {
        _ = bufsize;
        if (channel < 0 || channel > 2) return canStatus.canERR_PARAM;
        if (buffer == null || buffer.Length < 2) return canStatus.canERR_PARAM;
        if (item == canCHANNELDATA_CARD_SERIAL_NO || item == canCHANNELDATA_CARD_UPC_NO)
        {
            // Fake hi/lo values
            buffer[0] = (uint)(0xABC00000u + (uint)channel); // lo
            buffer[1] = 0x00001234u; // hi
            return canStatus.canOK;
        }
        return canStatus.canERR_NOT_IMPLEMENTED;
    }

    public static canStatus canGetChannelData_UInt64Array(int channel, int item, ulong[] buffer, UIntPtr bufsize)
    {
        _ = channel; _ = item; _ = buffer; _ = bufsize; return canStatus.canERR_NOT_IMPLEMENTED;
    }

    public static canStatus canGetChannelData_Ansi(int channel, int item, StringBuilder buffer, UIntPtr bufsize)
    {
        _ = bufsize;
        if (channel < 0 || channel > 2) return canStatus.canERR_PARAM;
        if (item == canCHANNELDATA_CHANNEL_NAME)
        {
            buffer.Clear();
            buffer.Append($"kvch{channel}");
            return canStatus.canOK;
        }
        return canStatus.canERR_NOT_IMPLEMENTED;
    }

    public static canStatus canGetChannelData_Wide(int channel, int item, StringBuilder buffer, UIntPtr bufsize)
    { return canGetChannelData_Ansi(channel, item, buffer, bufsize); }

    public static canStatus canGetChannelData_Bytes(int channel, int item, byte[] buffer, UIntPtr bufsize)
    { _ = channel; _ = item; _ = buffer; _ = bufsize; return canStatus.canERR_NOT_IMPLEMENTED; }

    // Convenience wrappers (match real Canlib.cs)
    public static canStatus canGetErrorText(canStatus err, out string msg)
    {
        msg = err.ToString();
        return canStatus.canOK;
    }

    public static canStatus GetChannelName(int channel, out string name)
    {
        var sb = new StringBuilder(256);
        var st = canGetChannelData_Ansi(channel, canCHANNELDATA_CHANNEL_NAME, sb, (UIntPtr)256u);
        name = st == canStatus.canOK ? sb.ToString() : string.Empty;
        return st;
    }

    public static canStatus GetUInt32(int channel, int item, out uint value)
    {
        return canGetChannelData_UInt32(channel, item, out value, (UIntPtr)4u);
    }

    public static canStatus GetUInt32Pair(int channel, int item, out uint hi, out uint lo)
    {
        var arr = new uint[2];
        var st = canGetChannelData_UInt32Array(channel, item, arr, (UIntPtr)(2 * sizeof(uint)));
        hi = arr[1];
        lo = arr[0];
        return st;
    }

    public static canStatus GetChannelCapEx(int channel, out ulong word0, out ulong word1)
    {
        var arr = new ulong[2];
        var st = canGetChannelData_UInt64Array(channel, canCHANNELDATA_CHANNEL_CAP, arr, (UIntPtr)(2 * sizeof(ulong)));
        word0 = arr.Length > 0 ? arr[0] : 0;
        word1 = arr.Length > 1 ? arr[1] : 0;
        return st;
    }

    public static canStatus GetEanString(int channel, int item, out string ean)
    {
        ean = string.Empty;
        var st = GetUInt32Pair(channel, item, out uint hi, out uint lo);
        if (st != canStatus.canOK) return st;
        ulong v = ((ulong)hi << 32) | lo;
        string digits = v.ToString("D13");
        ean = $"{digits.Substring(0, 2)}-{digits.Substring(2, 5)}-{digits.Substring(7, 5)}-{digits.Substring(12, 1)}";
        return st;
    }

    // Optional: kvSetTimerScale for reflection-based probing
    public static canStatus kvSetTimerScale(int hnd, int scale)
    {
        if (!TryGetHandle(hnd, out var h)) return canStatus.canERR_INVHANDLE;
        h.TimerScaleUs = scale;
        return canStatus.canOK;
    }
}

#endif

