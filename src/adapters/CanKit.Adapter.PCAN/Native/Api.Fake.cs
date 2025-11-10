// FAKE PCAN-Basic API and types for unit tests
#if FAKE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Adapter.PCAN.Native;
using Microsoft.Win32.SafeHandles;

namespace Peak.Can.Basic.BackwardCompatibility
{
    [Flags]
    public enum TPCANMessageType : byte
    {
        PCAN_MESSAGE_STANDARD = 0x00,
        PCAN_MESSAGE_RTR = 0x01,
        PCAN_MESSAGE_EXTENDED = 0x02,
        PCAN_MESSAGE_FD = 0x10,
        PCAN_MESSAGE_BRS = 0x20,
        PCAN_MESSAGE_ESI = 0x40,
        PCAN_MESSAGE_ERRFRAME = 0x80
    }

    public struct TPCANMsg
    {
        public uint ID;
        public TPCANMessageType MSGTYPE;
        public byte LEN;
        public byte[] DATA;
        public TPCANMsg(uint id, TPCANMessageType t, byte len, byte[] data)
        {
            ID = id; MSGTYPE = t; LEN = len; DATA = new byte[8];
            if (data != null)
            {
                var copy = Math.Min(8, Math.Min(len, data.Length));
                Array.Copy(data, 0, DATA, 0, copy);
            }
        }
    }

    public struct TPCANMsgFD
    {
        public uint ID;
        public TPCANMessageType MSGTYPE;
        public byte DLC;
        public byte[] DATA;
        public TPCANMsgFD(uint id, TPCANMessageType t, byte dlc, byte[] data)
        {
            ID = id; MSGTYPE = t; DLC = dlc; DATA = new byte[64];
            var len = CanFrame.DlcToLen(dlc);
            var copy = Math.Min(64, Math.Min(len, data.Length));
            Array.Copy(data, 0, DATA, 0, copy);
        }
    }

    public enum TPCANStatus : uint
    {
        PCAN_ERROR_OK = 0x00000,
        PCAN_ERROR_QRCVEMPTY = 0x00200
    }
}

namespace Peak.Can.Basic
{
    using BackwardCompatibility;

    [Flags]
    public enum MessageType : byte
    {
        Standard = 0,
        RemoteRequest = 1,
        Extended = 2,
        FlexibleDataRate = 4,
        BitRateSwitch = 8,
        ErrorStateIndicator = 16,
        Echo = 32,
        Error = 64,
        Status = 128
    }
    [Flags]
    public enum PcanStatus : uint
    {
        OK = 0,
        TransmitBufferFull = 1,
        BusWarning = 8,
        BusOff = 16,
        TransmitQueueFull = 128,
        BusPassive = 262_144,

    }

    public enum PcanChannel : ushort
    {
        NoneBus = 0,
        Usb01 = 1281,
        Usb02 = 1282,
        Usb03 = 1283
    }

    public enum PcanParameter
    {
        MessageFilter = 4,
        ReceiveEvent = 3,
        ListenOnly = 8,
        AllowEchoFrames = 44,
        ChannelCondition = 13,
        ChannelFeatures = 22,
        AllowErrorFrames = 32,
    }

    public static class ParameterValue
    {
        public enum Activation : uint
        {
            Off = 0,
            On = 1
        }

        public enum Filter : uint
        {
            Open,
            Close,
            Custom
        }
    }

    [Flags]
    public enum PcanDeviceFeatures : uint
    {
        FlexibleDataRate = 1
    }

    [Flags]
    public enum ChannelCondition : uint
    {
        ChannelAvailable = 1
    }

    public enum FilterMode
    {
        Standard = 0,
        Extended = 1
    }

    public enum PcanChannelDeviceType
    {
        Usb = 0
    }

    public struct PcanChannelInformation
    {
        public PcanChannel ChannelHandle { get; set; }
        public string DeviceName { get; set; }
        public PcanChannelDeviceType DeviceType { get; set; }
    }

    public enum Bitrate
    {
        Pcan1000,
        Pcan800,
        Pcan500,
        Pcan250,
        Pcan125,
        Pcan100,
        Pcan95,
        Pcan83,
        Pcan50,
        Pcan47,
        Pcan33,
        Pcan20,
        Pcan10,
        Pcan5
    }

    public struct BitrateFD
    {
        public enum ClockFrequency : uint
        {
            _40000000 = 40_000_000,
            _80000000 = 80_000_000
        }

        public struct BitrateSegment
        {
            public uint Tseg1, Tseg2, Brp, Sjw;
            public BitrateType Mode;
        }

        public enum BitrateType
        {
            ArbitrationPhase,
            DataPhase
        }

        public ClockFrequency Clock;
        public BitrateSegment Nominal;
        public BitrateSegment Data;

        public BitrateFD(ClockFrequency clock, BitrateSegment nominal, BitrateSegment data)
        {
            Clock = clock; Nominal = nominal; Data = data;
        }

        public override string ToString()
        {
            // "f_clock=80000000,nom_brp=2,nom_tseg1=63,nom_tseg2=16,nom_sjw=16,
            //  data_brp=2,data_tseg1=15,data_tseg2=4,data_sjw=4"
            var fclk = ((uint)Clock).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"f_clock={fclk}," +
                   $"nom_brp={Nominal.Brp},nom_tseg1={Nominal.Tseg1},nom_tseg2={Nominal.Tseg2},nom_sjw={Nominal.Sjw}," +
                   $"data_brp={Data.Brp},data_tseg1={Data.Tseg1},data_tseg2={Data.Tseg2},data_sjw={Data.Sjw}";
        }

        public static implicit operator string(BitrateFD fd) => fd.ToString();
    }

    public static class Api
    {
        private sealed class Frame
        {
            public uint Id;
            public TPCANMessageType Type;
            public byte[] Data = Array.Empty<byte>();
            public bool IsFd;
            public ulong TsUs;
        }

        private sealed class ChannelState
        {
            public readonly ConcurrentQueue<Frame> Rx = new();
            public readonly List<(uint from, uint to)> StdFilters = new();
            public readonly List<(uint from, uint to)> ExtFilters = new();
            public ParameterValue.Filter FilterStatus = ParameterValue.Filter.Open;
            public EventWaitHandle? RecEvent;
            public bool Initialized;
            public bool FdMode;
            public bool AllowErr;
            public uint NominalBitrate;
            public uint DataBitrate;
        }

        private static class World
        {
            public static readonly object Gate = new();
            public static readonly Dictionary<PcanChannel, ChannelState> Ch = new()
            {
                [PcanChannel.Usb01] = new ChannelState(),
                [PcanChannel.Usb02] = new ChannelState(),
                [PcanChannel.Usb03] = new ChannelState()
            };

            public static ChannelState Get(PcanChannel c) => Ch[c];
            public static ChannelState Set(PcanChannel c, ChannelState s) => Ch[c] = s;
            public static IEnumerable<KeyValuePair<PcanChannel, ChannelState>> All() => Ch;
        }

        private static bool Match(ChannelState s, bool ext, uint id)
        {
            if (s.FilterStatus == ParameterValue.Filter.Close)
                return false;
            if (s.FilterStatus == ParameterValue.Filter.Open)
                return true;
            var list = ext ? s.ExtFilters : s.StdFilters;
            if (list.Count == 0) return true;
            foreach (var (from, to) in list)
            {
                if (id >= from && id <= to) return true;
            }
            return false;
        }

        private static uint MapClassicBitrate(Bitrate b)
        {
            switch (b)
            {
                case Bitrate.Pcan1000: return 1_000_000;
                case Bitrate.Pcan800: return 800_000;
                case Bitrate.Pcan500: return 500_000;
                case Bitrate.Pcan250: return 250_000;
                case Bitrate.Pcan125: return 125_000;
                case Bitrate.Pcan100: return 100_000;
                case Bitrate.Pcan95: return 95_000;
                case Bitrate.Pcan83: return 83_000;
                case Bitrate.Pcan50: return 50_000;
                case Bitrate.Pcan47: return 47_000;
                case Bitrate.Pcan33: return 33_000;
                case Bitrate.Pcan20: return 20_000;
                case Bitrate.Pcan10: return 10_000;
                case Bitrate.Pcan5: return 5_000;
                default: return 0;
            }
        }

        private static uint ComputeRate(BitrateFD.BitrateSegment seg, BitrateFD.ClockFrequency clock)
        {
            if (seg.Brp == 0) return 0;
            var tq = 1u + seg.Tseg1 + seg.Tseg2;
            if (tq == 0) return 0;
            var clk = (uint)clock;
            return clk / (seg.Brp * tq);
        }

        private static void Emit(PcanChannel src, Frame f)
        {
            var srcState = World.Get(src);
            foreach (var kv in World.All())
            {
                if (kv.Key == src) continue; // no echo
                var s = kv.Value;
                // Bitrate compatibility: require same nominal bitrate when known
                if (srcState.NominalBitrate != 0 && s.NominalBitrate != 0 && srcState.NominalBitrate != s.NominalBitrate)
                    continue;
                // CAN FD gating and data bitrate matching
                bool isFd = (f.Type & TPCANMessageType.PCAN_MESSAGE_FD) != 0;
                if (isFd)
                {
                    if (!s.FdMode) continue;
                    if (srcState.DataBitrate != 0 && s.DataBitrate != 0 && srcState.DataBitrate != s.DataBitrate)
                        continue;
                }
                bool ext = (f.Type & TPCANMessageType.PCAN_MESSAGE_EXTENDED) != 0;
                if (!Match(s, ext, f.Id)) continue;
                var copy = new Frame { Id = f.Id, Type = f.Type, Data = f.Data.ToArray(), IsFd = f.IsFd, TsUs = f.TsUs };
                s.Rx.Enqueue(copy);
                try { s.RecEvent?.Set(); } catch { }
            }
        }

        public static PcanStatus Initialize(PcanChannel channel, Bitrate bitrate)
        {
            World.Set(channel, new ChannelState()
            {
                Initialized = true,
                FdMode = false,
                NominalBitrate = MapClassicBitrate(bitrate),
                DataBitrate = 0
            });
            return PcanStatus.OK;
        }

        public static PcanStatus Initialize(PcanChannel channel, BitrateFD fd)
        {
            World.Set(channel, new ChannelState()
            {
                Initialized = true,
                FdMode = true,
                NominalBitrate = ComputeRate(fd.Nominal, fd.Clock),
                DataBitrate = ComputeRate(fd.Data, fd.Clock)
            });
            return PcanStatus.OK;
        }

        public static PcanStatus Uninitialize(PcanChannel channel)
        {
            World.Get(channel).Initialized = false;
            return PcanStatus.OK;
        }

        public static PcanStatus Reset(PcanChannel channel)
        {
            var s = World.Get(channel);
            while (s.Rx.TryDequeue(out _)) { }
            return PcanStatus.OK;
        }

        public static PcanStatus GetStatus(PcanChannel _)
        { return PcanStatus.OK; }

        public static PcanStatus SetValue(PcanChannel channel, PcanParameter param, ParameterValue.Activation value)
        {
            var s = World.Get(channel);
            if (param == PcanParameter.AllowErrorFrames) { s.AllowErr = value == ParameterValue.Activation.On; return PcanStatus.OK; }

            return PcanStatus.OK;
        }

        public static PcanStatus SetValue(PcanChannel channel, PcanParameter param, int value)
        {
            if (param == PcanParameter.ReceiveEvent)
            {
                // Wrap existing kernel event handle
                try
                {
                    var ev = new EventWaitHandle(false, EventResetMode.AutoReset);
                    ev.SafeWaitHandle?.Dispose();
                    ev.SafeWaitHandle = new SafeWaitHandle(new IntPtr(value), false);
                    World.Get(channel).RecEvent = ev;
                    return PcanStatus.OK;
                }
                catch { return PcanStatus.OK; }
            }
            return PcanStatus.OK;
        }

        public static PcanStatus SetValue(PcanChannel channel, PcanParameter param, ParameterValue.Filter value)
        {
            var s = World.Get(channel);
            if (param == PcanParameter.MessageFilter && value == ParameterValue.Filter.Close)
            {
                s.FilterStatus = value;
                if (s.FilterStatus != ParameterValue.Filter.Custom)
                {
                    s.ExtFilters.Clear();
                    s.StdFilters.Clear();
                }
            }
            return PcanStatus.OK;
        }

        public static PcanStatus SetValue(PcanChannel channel, PcanParameter param, uint value)
        {
            // Forward to int overload (handles 0 and real handles)
            return SetValue(channel, param, unchecked((int)value));
        }

        public static PcanStatus GetValue(PcanChannel channel, PcanParameter param, out uint raw)
        {
            raw = 0;
            if (param == PcanParameter.ChannelCondition)
            {
                raw = (uint)ChannelCondition.ChannelAvailable; return PcanStatus.OK;
            }
            if (param == PcanParameter.ChannelFeatures)
            {
                raw = (uint)PcanDeviceFeatures.FlexibleDataRate; return PcanStatus.OK;
            }
            if (param == PcanParameter.ReceiveEvent)
            {
                // Create a kernel event and return its handle
                var ev = new EventWaitHandle(false, EventResetMode.AutoReset);
                World.Get(channel).RecEvent = ev;
                raw = (uint)ev.SafeWaitHandle.DangerousGetHandle().ToInt32();
            }
            return PcanStatus.OK;
        }

        public static PcanStatus FilterMessages(PcanChannel channel, uint from, uint to, FilterMode mode)
        {
            var s = World.Get(channel);
            s.FilterStatus = ParameterValue.Filter.Custom;
            var list = mode == FilterMode.Extended ? s.ExtFilters : s.StdFilters;
            list.Add((from, to));
            return PcanStatus.OK;
        }

        public static PcanStatus GetAttachedChannels(out PcanChannelInformation[] chans)
        {
            chans = new[]
            {
                new PcanChannelInformation{ ChannelHandle = PcanChannel.Usb01, DeviceName = "PCAN-USB", DeviceType = PcanChannelDeviceType.Usb },
                new PcanChannelInformation{ ChannelHandle = PcanChannel.Usb02, DeviceName = "PCAN-USB", DeviceType = PcanChannelDeviceType.Usb },
                new PcanChannelInformation{ ChannelHandle = PcanChannel.Usb03, DeviceName = "PCAN-USB", DeviceType = PcanChannelDeviceType.Usb },
            };
            return PcanStatus.OK;
        }

        // Internal helpers for fake native
        internal static unsafe bool TryDequeue(PcanChannel ch, out PcanBasicNative.TpcanMsg msg, out uint micros)
        {
            msg = default; micros = 0;
            var s = World.Get(ch);
            if (s.Rx.TryDequeue(out var f))
            {
                var len = (byte)Math.Min(8, f.Data.Length);
                msg = new PcanBasicNative.TpcanMsg()
                {
                    ID = f.Id,
                    MSGTYPE = f.Type,
                    LEN = len
                };
                fixed (byte* src = f.Data)
                fixed (byte* dst = msg.DATA)
                {
                    Unsafe.CopyBlock(dst, src, len);
                }
                micros = (uint)(f.TsUs % uint.MaxValue);
                return true;
            }
            return false;
        }

        internal static unsafe bool TryDequeueFd(PcanChannel ch, out PcanBasicNative.TpcanMsgFd msg, out ulong micros)
        {
            msg = default; micros = 0;
            var s = World.Get(ch);
            if (s.Rx.TryDequeue(out var f))
            {
                byte dlc;
                if ((f.Type & TPCANMessageType.PCAN_MESSAGE_FD) != 0)
                {
                    var l = Math.Min(64, f.Data.Length);
                    dlc = CanFrame.LenToDlc(l);
                }
                else
                {
                    dlc = (byte)Math.Min(8, f.Data.Length);
                }

                msg = new PcanBasicNative.TpcanMsgFd
                {
                    ID = f.Id,
                    DLC = dlc,
                    MSGTYPE = f.Type
                };
                fixed (byte* src = f.Data)
                fixed (byte* dst = msg.DATA)
                {
                    Unsafe.CopyBlockUnaligned(dst, src,
                        (uint)CanFrame.DlcToLen(dlc));
                }
                micros = f.TsUs;
                return true;
            }
            return false;
        }

        internal static void Submit(PcanChannel src, uint id, TPCANMessageType type, byte[] data, bool isFd)
        {
            var f = new Frame { Id = id, Type = type, Data = data.ToArray(), IsFd = isFd, TsUs = (ulong)(DateTime.UtcNow.Ticks / 10) };
            Emit(src, f);
        }
    }
}

#endif
