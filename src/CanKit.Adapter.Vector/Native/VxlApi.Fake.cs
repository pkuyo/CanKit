#if FAKE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CanKit.Adapter.Vector.Native;

/// <summary>
/// FAKE backend used for tests when the real Vector driver is unavailable.
/// Provides a very small in-memory emulation layer matching the real P/Invoke signatures.
/// </summary>
internal static class VxlApi
{
    public const int RX_BATCH_COUNT = 256;
    public const int TX_BATCH_COUNT = 32;
    public const int XL_SUCCESS = 0;
    public const int XL_ERR_QUEUE_IS_EMPTY = 10;
    public const int XL_ERR_INVALID_PORT = 118;
    public const int XL_ERR_HW_NOT_PRESENT = 129;
    public const int XL_INTERFACE_VERSION = 3;
    public const int XL_INTERFACE_VERSION_V4 = 4;
    public const ushort XL_CAN_EV_TAG_RX_OK = 0x0400;
    public const ushort XL_CAN_EV_TAG_RX_ERROR = 0x0401;
    public const ushort XL_CAN_EV_TAG_TX_ERROR = 0x0402;
    public const ushort XL_CAN_EV_TAG_TX_OK = 0x0404;
    public const ushort XL_CAN_EV_TAG_CHIP_STATE = 0x0409;
    public const ushort XL_CAN_EV_TAG_TX_MSG = 0x0440;
    public const ushort XL_CAN_EV_TAG_TX_ERRFR = 0x0441;

    public const byte XL_EVENT_TAG_TRANSMIT_MSG = 0x0A;

    public const uint XL_CAN_EXT_MSG_ID = 0x8000_0000u;
    public const uint XL_CAN_RXMSG_FLAG_EDL = 0x0001;
    public const uint XL_CAN_RXMSG_FLAG_BRS = 0x0002;
    public const uint XL_CAN_RXMSG_FLAG_ESI = 0x0004;
    public const uint XL_CAN_RXMSG_FLAG_RTR = 0x0010;
    public const uint XL_CAN_RXMSG_FLAG_EF = 0x0200;
    public const uint XL_CAN_RXMSG_FLAG_ARB_LOST = 0x0400;
    public const uint XL_CAN_RXMSG_FLAG_WAKEUP = 0x2000;
    public const uint XL_CAN_RXMSG_FLAG_TE = 0x4000;

    public const ushort XL_CAN_MSG_FLAG_REMOTE_FRAME = 0x0010;

    public const uint XL_CAN_TXMSG_FLAG_EDL = 0x0001;
    public const uint XL_CAN_TXMSG_FLAG_BRS = 0x0002;
    public const uint XL_CAN_TXMSG_FLAG_RTR = 0x0010;
    public const uint XL_CAN_TXMSG_FLAG_HIGHPRIO = 0x0080;
    public const uint XL_CAN_TXMSG_FLAG_WAKEUP = 0x0200;

    public const byte XL_OUTPUT_MODE_SILENT = 0;
    public const byte XL_OUTPUT_MODE_NORMAL = 1;

    public const uint XL_BUS_TYPE_CAN = 0x0000_0001;
    public const uint XL_BUS_COMPATIBLE_CAN = 0x0000_0001;
    public const uint XL_BUS_ACTIVE_CAP_CAN = 0x0001_0000;
    public const uint XL_CHANNEL_FLAG_CANFD_BOSCH_SUPPORT = 0x2000_0000;
    public const uint XL_CHANNEL_FLAG_CANFD_ISO_SUPPORT = 0x8000_0000;

    public const ushort XL_CHIPSTAT_BUSOFF = 0x0001;
    public const ushort XL_CHIPSTAT_ERROR_PASSIVE = 0x0002;
    public const ushort XL_CHIPSTAT_ERROR_WARNING = 0x0004;
    public const ushort XL_CHIPSTAT_ERROR_ACTIVE = 0x0008;

    public static ulong ChannelIndexToMask(int index) => 1UL << index;

    public static int SizeOfCanRxEvent => Marshal.SizeOf<XLcanRxEvent>();
    public static int SizeOfCanTxEvent => Marshal.SizeOf<XLcanTxEvent>();

    #region Struct definitions (subset of real interop)

    [StructLayout(LayoutKind.Sequential)]
    public struct XLchipParams
    {
        public uint BitRate;
        public byte Sjw;
        public byte Tseg1;
        public byte Tseg2;
        public byte Sam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLcanFdConf
    {
        public uint ArbitrationBitRate;
        public uint SjwAbr;
        public uint Tseg1Abr;
        public uint Tseg2Abr;
        public uint DataBitRate;
        public uint SjwDbr;
        public uint Tseg1Dbr;
        public uint Tseg2Dbr;
        public byte Reserved;
        public byte Options;
        public ushort Reserved1;
        public uint Reserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanTxMsg
    {
        public uint CanId;
        public uint MsgFlags;
        public byte Dlc;
        public fixed byte Reserved[7];
        public fixed byte Data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLcanTxTagData
    {
        public XLcanTxMsg CanMsg;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanTxEvent
    {
        public ushort Tag;
        public ushort TransId;
        public byte ChanIndex;
        public fixed byte Reserved[3];
        public XLcanTxTagData TagData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanRxMsg
    {
        public uint CanId;
        public uint MsgFlags;
        public uint Crc;
        public fixed byte Reserved1[12];
        public ushort TotalBitCnt;
        public byte Dlc;
        public fixed byte Reserved2[5];
        public fixed byte Data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanTxRequest
    {
        public uint CanId;
        public uint MsgFlags;
        public byte Dlc;
        public byte TxAttemptConf;
        public ushort Reserved;
        public fixed byte Data[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanRxError
    {
        public byte ErrorCode;
        public fixed byte Reserved[95];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLchipState
    {
        public byte BusStatus;
        public byte TxErrorCounter;
        public byte RxErrorCounter;
        public byte Reserved;
        public uint Reserved0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct XLchipStateBasic
    {
        public byte BusStatus;
        public byte TxErrorCounter;
        public byte RxErrorCounter;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct XLcanRxTagData
    {
        [FieldOffset(0)]
        public XLcanRxMsg CanRxOkMsg;

        [FieldOffset(0)]
        public XLcanRxMsg CanTxOkMsg;

        [FieldOffset(0)]
        public XLcanTxRequest CanTxRequest;

        [FieldOffset(0)]
        public XLcanRxError CanError;

        [FieldOffset(0)]
        public XLchipState ChipState;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLcanRxEvent
    {
        public int Size;
        public ushort Tag;
        public byte ChanIndex;
        public byte Reserved;
        public int UserHandle;
        public ushort FlagsChip;
        public ushort Reserved0;
        public ulong Reserved1;
        public ulong TimeStamp;
        public XLcanRxTagData TagData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XLcanMsg
    {
        public uint Id;
        public ushort Flags;
        public ushort Dlc;
        public ulong Res1;
        public fixed byte Data[8];
        public ulong Res2;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct XLeventTagData
    {
        [FieldOffset(0)]
        public XLcanMsg Msg;

        [FieldOffset(0)]
        public XLchipStateBasic ChipState;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct XLevent
    {
        public byte Tag;
        public byte ChanIndex;
        public ushort TransId;
        public ushort PortHandle;
        public byte Flags;
        public byte Reserved;
        public ulong TimeStamp;
        public XLeventTagData TagData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XLbusParams
    {
        public uint BusType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)]
        public byte[] Data;
    }

    // Align with real interop shape
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct XLchannelConfig
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public byte HwType;
        public byte HwIndex;
        public byte HwChannel;
        public ushort TransceiverType;
        public ushort TransceiverState;
        public ushort ConfigError;
        public byte ChannelIndex;
        public ulong ChannelMask;
        public uint ChannelCapabilities;
        public uint ChannelBusCapabilities;
        public byte IsOnBus;
        public uint ConnectedBusType;
        public XLbusParams BusParams;
        public uint DoNotUse;
        public uint DriverVersion;
        public uint InterfaceVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] RawData;

        public uint SerialNumber;
        public uint ArticleNumber;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string TransceiverName;

        public uint SpecialCabFlags;
        public uint DominantTimeout;
        public byte DominantRecessiveDelay;
        public byte RecessiveDominantDelay;
        public byte ConnectionInfo;
        public byte CurrentlyAvailableTimestamps;
        public ushort MinimalSupplyVoltage;
        public ushort MaximalSupplyVoltage;
        public uint MaximalBaudrate;
        public byte FpgaCoreCapabilities;
        public byte SpecialDeviceStatus;
        public ushort ChannelBusActiveCapabilities;
        public ushort BreakOffset;
        public ushort DelimiterOffset;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct XLdriverConfig
    {
        public uint DllVersion;
        public uint ChannelCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] Reserved;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public XLchannelConfig[] Channel;
    }

    #endregion
    public enum XLeventType : byte
    {
        XL_NO_COMMAND = 0,
        XL_RECEIVE_MSG = 1,
        XL_CHIP_STATE  = 4,
        XL_TRANSCEIVER = 6,
        XL_TIMER = 8,
        XL_TRANSMIT_MSG = 10,
        XL_SYNC_PULSE = 11,
        XL_APPLICATION_NOTIFICATION = 15,

    };
    private sealed class Handle
    {
        public int Id;
        public ulong Mask;
        public bool Active;
        public byte OutputMode = XL_OUTPUT_MODE_NORMAL;
        public readonly ConcurrentQueue<XLcanRxEvent> RxQueue = new();
        public string AppName = string.Empty;
        public IntPtr NotificationEvent;

        // Timing/config
        public uint ClassicBitrate;
        public bool FdConfigured;
        public uint FdArbBitrate;
        public uint FdDataBitrate;

        // Hardware acceptance filters
        public readonly List<(uint code, uint mask)> StdMasks = new();
        public readonly List<(uint code, uint mask)> ExtMasks = new();
        public readonly List<(uint from, uint to)> StdRanges = new();
    }

    private static class World
    {
        public static int NextHandle = 1;
        public static readonly object Gate = new();
        public static readonly Dictionary<int, Handle> Handles = new();
    }

    // no OS interop needed in FAKE

    public static string GetErrorString(int status) => $"FAKE_STATUS_{status}";

    public static int xlOpenDriver() => XL_SUCCESS;
    public static int xlCloseDriver() => XL_SUCCESS;

    public static int xlGetDriverConfig(ref XLdriverConfig config)
    {
        config.DllVersion = 0x0100;
        config.ChannelCount = 2;
        config.Reserved ??= new uint[10];
        config.Channel ??= CreateChannelArray();
        config.Channel[0] = new XLchannelConfig
        {
            Name = "FAKE_VECTOR_CH0",
            HwType = 0,
            HwIndex = 0,
            HwChannel = 0,
            ChannelIndex = 0,
            ChannelMask = ChannelIndexToMask(0),
            ChannelCapabilities = XL_CHANNEL_FLAG_CANFD_ISO_SUPPORT | XL_CHANNEL_FLAG_CANFD_BOSCH_SUPPORT,
            ChannelBusCapabilities = XL_BUS_COMPATIBLE_CAN | XL_BUS_ACTIVE_CAP_CAN,
            ChannelBusActiveCapabilities = 0,
            ConnectedBusType = XL_BUS_TYPE_CAN,
            BusParams = new XLbusParams { BusType = XL_BUS_TYPE_CAN, Data = new byte[28] },
            RawData = new uint[10],
            Reserved = new uint[3]
        };
        config.Channel[1] = new XLchannelConfig
        {
            Name = "FAKE_VECTOR_CH1",
            HwType = 0,
            HwIndex = 0,
            HwChannel = 1,
            ChannelIndex = 1,
            ChannelMask = ChannelIndexToMask(1),
            ChannelCapabilities = XL_CHANNEL_FLAG_CANFD_ISO_SUPPORT | XL_CHANNEL_FLAG_CANFD_BOSCH_SUPPORT,
            ChannelBusCapabilities = XL_BUS_COMPATIBLE_CAN | XL_BUS_ACTIVE_CAP_CAN,
            ChannelBusActiveCapabilities = 0,
            ConnectedBusType = XL_BUS_TYPE_CAN,
            BusParams = new XLbusParams { BusType = XL_BUS_TYPE_CAN, Data = new byte[28] },
            RawData = new uint[10],
            Reserved = new uint[3]
        };
        for (int i = 1; i < config.Channel.Length; i++)
        {
            if (i != 1)
                config.Channel[i] = default;
        }
        return XL_SUCCESS;
    }

    private static XLchannelConfig[] CreateChannelArray()
    {
        var arr = new XLchannelConfig[64];
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i].RawData = new uint[10];
            arr[i].Reserved = new uint[3];
            arr[i].BusParams = new XLbusParams { Data = new byte[28] };
        }
        return arr;
    }

    public static int xlOpenPort(ref int portHandle, string appName, ulong accessMask, ref ulong permissionMask, uint rxQueueSize, uint xlInterfaceVersion, uint busType)
    {
        lock (World.Gate)
        {
            var handle = new Handle
            {
                Id = World.NextHandle++,
                Mask = accessMask,
                AppName = appName ?? string.Empty
            };
            World.Handles[handle.Id] = handle;
            portHandle = handle.Id;
            permissionMask = accessMask;
            return XL_SUCCESS;
        }
    }

    public static int xlClosePort(int portHandle)
    {
        lock (World.Gate)
        {
            World.Handles.Remove(portHandle);
            return XL_SUCCESS;
        }
    }

    public static int xlActivateChannel(int portHandle, ulong accessMask, uint busType, uint flags)
        => World.Handles.TryGetValue(portHandle, out var handle) ? (handle.Active = true, XL_SUCCESS).Item2 : XL_ERR_HW_NOT_PRESENT;

    public static int xlDeactivateChannel(int portHandle, ulong accessMask)
        => World.Handles.TryGetValue(portHandle, out var handle) ? (handle.Active = false, XL_SUCCESS).Item2 : XL_ERR_HW_NOT_PRESENT;

    public static int xlCanSetChannelBitrate(int portHandle, ulong accessMask, uint bitrate)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        handle.ClassicBitrate = bitrate;
        return XL_SUCCESS;
    }

    public static int xlCanSetChannelParams(int portHandle, ulong accessMask, ref XLchipParams parameters)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        if (parameters.BitRate != 0)
            handle.ClassicBitrate = parameters.BitRate;
        return XL_SUCCESS;
    }

    public static int xlCanFdSetConfiguration(int portHandle, ulong accessMask, ref XLcanFdConf conf)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        handle.FdConfigured = true;
        handle.FdArbBitrate = conf.ArbitrationBitRate;
        handle.FdDataBitrate = conf.DataBitRate;
        // set nominal to arbitration to enable classic interop checks
        handle.ClassicBitrate = conf.ArbitrationBitRate;
        return XL_SUCCESS;
    }

    public static int xlCanSetChannelOutput(int portHandle, ulong accessMask, int outputMode)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        handle.OutputMode = (byte)outputMode;
        return XL_SUCCESS;
    }

    public static int xlCanSetChannelAcceptance(int portHandle, ulong accessMask, uint code, uint mask, uint acceptanceType)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        if (acceptanceType == 1)
        {
            // Special pattern seen in setup flows; ignore to prevent accidental filtering in FAKE
            if (!(code == 0xFFF && mask == 0xFFF))
                handle.StdMasks.Add((code, mask));
        }
        else if (acceptanceType == 2)
        {
            handle.ExtMasks.Add((code, mask));
        }
        return XL_SUCCESS;
    }
    public static int xlCanResetAcceptance(int portHandle, ulong accessMask, uint acceptanceType)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        if (acceptanceType == 1)
        {
            handle.StdMasks.Clear();
            handle.StdRanges.Clear();
        }
        else if (acceptanceType == 2)
        {
            handle.ExtMasks.Clear();
        }
        return XL_SUCCESS;
    }

    public static int xlCanAddAcceptanceRange(int portHandle, ulong accessMask, uint from, uint to)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        handle.StdRanges.Add((from, to));
        return XL_SUCCESS;
    }

    public static int xlFlushReceiveQueue(int portHandle)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        while (handle.RxQueue.TryDequeue(out _)) { }
        return XL_SUCCESS;
    }

    public static int xlCanReceive(int portHandle, out XLcanRxEvent ev)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
        {
            ev = default;
            return XL_ERR_HW_NOT_PRESENT;
        }

        if (handle.RxQueue.TryDequeue(out var item))
        {
            ev = item;
            return XL_SUCCESS;
        }

        ev = default;
        return XL_ERR_QUEUE_IS_EMPTY;
    }

    public static unsafe int xlReceive(int portHandle, ref int eventCount, XLevent* events)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;

        if (events == null || eventCount <= 0)
        {
            eventCount = 0;
            return XL_SUCCESS;
        }

        int produced = 0;
        while (produced < eventCount)
        {
            if (!handle.RxQueue.TryDequeue(out var rxEv))
                break;

            var msg = rxEv.TagData.CanRxOkMsg;
            var e = new XLevent
            {
                Tag = (byte)XLeventType.XL_RECEIVE_MSG,
                ChanIndex = rxEv.ChanIndex,
                TransId = 0,
                PortHandle = (ushort)portHandle,
                Flags = 0,
                Reserved = 0,
                TimeStamp = rxEv.TimeStamp,
                TagData = default
            };

            // Map RX message to generic event message
            e.TagData = new XLeventTagData
            {
                Msg = new XLcanMsg()
            };

            e.TagData.Msg.Id = msg.CanId;
            e.TagData.Msg.Flags = (ushort)(msg.MsgFlags & 0xFFFF);
            var dlc = (ushort)Math.Min(msg.Dlc, (byte)8);
            e.TagData.Msg.Dlc = dlc;
            unsafe
            {
                for (int i = 0; i < dlc; i++)
                {
                    e.TagData.Msg.Data[i] = msg.Data[i];
                }
            }

            events[produced] = e;
            produced++;
        }

        eventCount = produced;
        return produced > 0 ? XL_SUCCESS : XL_ERR_QUEUE_IS_EMPTY;
    }

    public static unsafe int xlCanTransmit(int portHandle, ulong accessMask, ref uint count, XLevent* events)
    {
        if (!World.Handles.TryGetValue(portHandle, out var sender))
            return XL_ERR_HW_NOT_PRESENT;

        if (events == null || count == 0)
        {
            count = 0;
            return XL_SUCCESS;
        }

        uint sent = 0;
        for (uint i = 0; i < count; i++)
        {
            var evt = events[i];
            if (evt.Tag != XL_EVENT_TAG_TRANSMIT_MSG)
                continue;

            var rx = new XLcanRxMsg
            {
                CanId = evt.TagData.Msg.Id,
                MsgFlags = (evt.TagData.Msg.Flags & XL_CAN_MSG_FLAG_REMOTE_FRAME) != 0 ? XL_CAN_RXMSG_FLAG_RTR : 0,
                Dlc = (byte)Math.Min((int)evt.TagData.Msg.Dlc, 8)
            };
            for (int b = 0; b < rx.Dlc; b++)
            {
                rx.Data[b] = evt.TagData.Msg.Data[b];
            }

            BroadcastToBus(sender, accessMask, evt.ChanIndex, ref rx);
            sent++;
        }
        count = sent;
        return XL_SUCCESS;
    }

    public static unsafe int xlCanTransmitEx(int portHandle, ulong accessMask, uint messageCount, ref uint sentCount, XLcanTxEvent* events)
    {
        if (!World.Handles.TryGetValue(portHandle, out var sender))
            return XL_ERR_HW_NOT_PRESENT;

        if (events == null || messageCount == 0)
        {
            sentCount = 0;
            return XL_SUCCESS;
        }

        uint sent = 0;
        for (uint i = 0; i < messageCount; i++)
        {
            var evt = events[i];
            if (evt.Tag != XL_CAN_EV_TAG_TX_MSG)
                continue;

            var rx = new XLcanRxMsg
            {
                CanId = evt.TagData.CanMsg.CanId,
                MsgFlags = evt.TagData.CanMsg.MsgFlags,
                Dlc = evt.TagData.CanMsg.Dlc
            };
            var len = Math.Min(rx.Dlc, (byte)64);
            for (int b = 0; b < len; b++) rx.Data[b] = evt.TagData.CanMsg.Data[b];

            BroadcastToBus(sender, accessMask, evt.ChanIndex, ref rx);
            sent++;
        }
        sentCount = sent;
        return XL_SUCCESS;
    }

    private static void BroadcastToBus(Handle sender, ulong accessMask, byte chanIndex, ref XLcanRxMsg rxMsg)
    {
        lock (World.Gate)
        {
            foreach (var kv in World.Handles)
            {
                var target = kv.Value;
                if (target.Id == sender.Id) continue; // no self-loopback
                if (!target.Active) continue; // only active channels
                if (!MasksOnSameBus(sender, target)) continue; // different bus

                // Sender silent (listen-only) cannot put frames on bus
                if (sender.OutputMode == XL_OUTPUT_MODE_SILENT)
                    continue;

                // Protocol/bitrate compatibility
                if (!IsCompatible(sender, target, rxMsg))
                    continue;

                // Hardware filters
                var isExt = (rxMsg.CanId & XL_CAN_EXT_MSG_ID) != 0;
                var id = rxMsg.CanId & 0x1FFF_FFFFu;

                if (isExt)
                {
                    if (!AcceptByMasks(id, target.ExtMasks))
                        continue;
                }
                else
                {
                    if (!AcceptByMasks(id, target.StdMasks) && !AcceptByRanges(id, target.StdRanges))
                    {
                        if (!(target.StdMasks.Count == 0 && target.StdRanges.Count == 0))
                            continue;
                    }
                }

                var ev = CreateRxEvent(ChannelIndexFromMask(target.Mask));
                ev.Tag = XL_CAN_EV_TAG_RX_OK;
                ev.TagData.CanRxOkMsg = rxMsg;
                target.RxQueue.Enqueue(ev);
                TrySignal(target.NotificationEvent);
            }
        }
    }

    private static readonly ulong LinkedGroup01 = ChannelIndexToMask(0) | ChannelIndexToMask(1);

    private static bool MasksOnSameBus(Handle sender, Handle target)
    {
        var a = sender.Mask;
        var b = target.Mask;
        if ((a & b) != 0) return true; // same channel
        // In FAKE, only link ch0<->ch1 when both app names are "virtual"
        if (!string.IsNullOrEmpty(sender.AppName) && !string.IsNullOrEmpty(target.AppName))
        {
            if (string.Equals(sender.AppName, "virtual", StringComparison.OrdinalIgnoreCase)
                && string.Equals(target.AppName, "virtual", StringComparison.OrdinalIgnoreCase))
            {
                return ((a & LinkedGroup01) != 0) && ((b & LinkedGroup01) != 0);
            }
        }
        return false;
    }

    private static byte ChannelIndexFromMask(ulong mask)
    {
        for (byte i = 0; i < 64; i++)
        {
            if (((mask >> i) & 1UL) != 0) return i;
        }
        return 0;
    }

    private static bool AcceptByMasks(uint id, List<(uint code, uint mask)> masks)
    {
        if (masks.Count == 0) return true;
        foreach (var (code, mask) in masks)
        {
            if ((id & mask) == (code & mask))
                return true;
        }
        return false;
    }

    private static bool AcceptByRanges(uint id, List<(uint from, uint to)> ranges)
    {
        if (ranges.Count == 0) return true;
        foreach (var (from, to) in ranges)
        {
            if (id >= from && id <= to)
                return true;
        }
        return false;
    }

    private static bool IsCompatible(Handle sender, Handle target, in XLcanRxMsg msg)
    {
        var isFdFrame = (msg.MsgFlags & XL_CAN_RXMSG_FLAG_EDL) != 0;
        if (isFdFrame)
        {
            if (!sender.FdConfigured || !target.FdConfigured) return false;
            return sender.FdArbBitrate == target.FdArbBitrate && sender.FdDataBitrate == target.FdDataBitrate;
        }
        else
        {
            var sNominal = sender.FdConfigured ? sender.FdArbBitrate : sender.ClassicBitrate;
            var tNominal = target.FdConfigured ? target.FdArbBitrate : target.ClassicBitrate;
            return sNominal != 0 && sNominal == tNominal;
        }
    }

    private static void TrySignal(IntPtr hEvent)
    {
        if (hEvent != IntPtr.Zero)
        {
            try { Win32.SetEvent(hEvent); } catch { }
        }
    }

    private static XLcanRxEvent CreateRxEvent(byte chanIndex)
    {
        var ev = new XLcanRxEvent
        {
            Size = SizeOfCanRxEvent,
            ChanIndex = chanIndex,
            TimeStamp = (ulong)Environment.TickCount,
        };
        ev.TagData = new XLcanRxTagData
        {
            CanRxOkMsg = default
        };
        return ev;
    }

    // Map appName/appChannel to hardware triple (hwType/hwIndex/hwChannel)
    public static int xlGetApplConfig(string appName, uint appChannel, ref uint hwType, ref uint hwIndex, ref uint hwChannel, uint busType)
    {
        // Only CAN bus type supported in FAKE
        if (busType != XL_BUS_TYPE_CAN) return XL_ERR_HW_NOT_PRESENT;
        if (string.Equals(appName, "virtual", StringComparison.OrdinalIgnoreCase))
        {
            if (appChannel == 0 || appChannel == 1)
            {
                hwType = 0; hwIndex = 0; hwChannel = appChannel;
                return XL_SUCCESS;
            }
        }
        return XL_ERR_HW_NOT_PRESENT;
    }

    // Convert hardware triple to global channel index (return index or -1)
    public static int xlGetChannelIndex(int hwType, int hwIndex, int hwChannel)
        => (hwType == 0 && hwIndex == 0 && (hwChannel == 0 || hwChannel == 1)) ? hwChannel : -1;

    public static int xlSetNotification(int portHandle, IntPtr hEvent)
    {
        if (!World.Handles.TryGetValue(portHandle, out var handle))
            return XL_ERR_HW_NOT_PRESENT;
        handle.NotificationEvent = hEvent;
        return XL_SUCCESS;
    }

    private static class Win32
    {
        [DllImport("kernel32.dll", SetLastError = false)]
        public static extern bool SetEvent(IntPtr hEvent);
    }
}
#endif
