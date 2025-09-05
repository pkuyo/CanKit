using System;
using System.Text;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    
    
    public interface ICanFrame
    {
        CanFrameType FrameKind { get; }
        uint RawID { get; init; }
        ReadOnlyMemory<byte> Data { get; init; }
        byte Dlc { get; }
        uint ID { get; init; }
    }
    
    internal static class CanIdBits
    {
        private const int EXT_BIT = 31;
        private const int RTR_BIT = 30;
        private const int ERR_BIT = 29;
        private const uint ID_MASK = 0x1FFFFFFF;

        public static uint GetId(uint raw) => raw & ID_MASK;
        public static uint SetId(uint raw, uint id) => (raw & ~ID_MASK) | (id & ID_MASK);

        public static bool Get(uint raw, int bit) => (raw & (1u << bit)) != 0;
        public static uint Set(uint raw, int bit, bool v)
            => v ? (raw | (1u << bit)) : (raw & ~(1u << bit));

        public static bool IsExtended(uint raw) => Get(raw, EXT_BIT);
        public static uint WithExtended(uint raw, bool v) => Set(raw, EXT_BIT, v);
        public static bool IsRemote(uint raw) => Get(raw, RTR_BIT);
        public static uint WithRemote(uint raw, bool v) => Set(raw, RTR_BIT, v);
        public static bool IsError(uint raw) => Get(raw, ERR_BIT);
        public static uint WithError(uint raw, bool v) => Set(raw, ERR_BIT, v);
    }
    
    public readonly record struct CanClassicFrame : ICanFrame
    {
        public static implicit operator CanTransmitData(CanClassicFrame value)
        {
            return new CanTransmitData()
            {
                canFrame = value
            };
        }
        public CanClassicFrame(uint rawIDInit, ReadOnlyMemory<byte> dataInit = default)
        {
            RawID = rawIDInit;
            _data = dataInit;
        }
        
        public CanClassicFrame(uint Id, ReadOnlyMemory<byte> dataInit = default, bool isExtendedFrame = false)
        {
            ID = Id;
            IsExtendedFrame = isExtendedFrame;
            _data = dataInit;
        }
        public CanFrameType FrameKind => CanFrameType.CanClassic;

        public uint RawID { get; init; }

        public uint ID
        {
            get => CanIdBits.GetId(RawID);
            init => RawID = CanIdBits.SetId(RawID, value);
        }

        public bool IsExtendedFrame
        {
            get => CanIdBits.IsExtended(RawID);
            init => RawID = CanIdBits.WithExtended(RawID, value);
        }
        public bool IsRemoteFrame
        {
            get => CanIdBits.IsRemote(RawID);
            init => RawID = CanIdBits.WithRemote(RawID, value);
        }
        public bool IsErrorFrame
        {
            get => CanIdBits.IsError(RawID);
            init => RawID = CanIdBits.WithError(RawID, value);
        }

        public  ReadOnlyMemory<byte> Data
        {
            get => _data;
            init =>  _data = Validate(value);
            
        }

        public byte Dlc => (byte)Data.Length;

        private static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            if (src.Length > 8) throw new ArgumentOutOfRangeException(nameof(Data),
                "Classic CAN frame data length cannot exceed 8 bytes.");
            return src;
        }
        
        private readonly ReadOnlyMemory<byte> _data;
    }
    

    
    public readonly struct CanFdFrame : ICanFrame
    {
        public CanFdFrame(uint rawIdInit, ReadOnlyMemory<byte> dataInit = default, bool BRS = false, bool ESI = false)
        {
            RawID = rawIdInit;
            _data = Validate(_data);
            BitRateSwitch = BRS;
            ErrorStateIndicator = ESI;
        }
        public CanFrameType FrameKind => CanFrameType.CanFd;

        public uint RawID { get; init; }

        public uint ID
        {
            get => CanIdBits.GetId(RawID);
            init => RawID = CanIdBits.SetId(RawID, value);
        }

        public bool IsExtendedFrame
        {
            get => CanIdBits.IsExtended(RawID);
            init => RawID = CanIdBits.WithExtended(RawID, value);
        }
        public bool IsRemoteFrame
        {
            get => CanIdBits.IsRemote(RawID);
            init => RawID = CanIdBits.WithRemote(RawID, value);
        }
        public bool IsErrorFrame
        {
            get => CanIdBits.IsError(RawID);
            init => RawID = CanIdBits.WithError(RawID, value);
        }

        public bool BitRateSwitch { get; init; }
        public bool ErrorStateIndicator { get; init; }

        public ReadOnlyMemory<byte> Data
        {
            get => _data;
            init => _data = Validate(value);
        }

        public byte Dlc => LenToDlc(Data.Length);

        public static int DlcToLen(byte dlc)
            => dlc <= 8 ? dlc : dlc switch
            {
                9 => 12, 10 => 16, 11 => 20, 12 => 24, 13 => 32, 14 => 48, 15 => 64,
                _ => throw new ArgumentOutOfRangeException(nameof(dlc))
            };

        public static byte LenToDlc(int len)
        {
            if (len < 0 || len > 64) throw new ArgumentOutOfRangeException(nameof(len));
            if (len <= 8) return (byte)len;
            return len switch
            {
                <= 12 => 9, <= 16 => 10, <= 20 => 11, <= 24 => 12, <= 32 => 13, <= 48 => 14, _ => 15,
            };
        }

        private static ReadOnlyMemory<byte> Validate(ReadOnlyMemory<byte> src)
        {
            _ = LenToDlc(src.Length); // 触发范围检查
            return src;
        }

        private readonly ReadOnlyMemory<byte> _data;
    }

    public readonly struct CanErrorFrame
    {
        public CanErrorCode ErrorCode { get; init; }
        public bool IsTransmit { get; init; }
        public DateTime Timestamp { get; init; }
        
        public ReadOnlyMemory<byte> RawData { get; init; }
        public int Channel { get; init; }

        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss.fff}] " +
                   $"Channel={Channel}, " +
                   $"Direction={(IsTransmit ? "Tx" : "Rx")}, " +
                   $"Error={ErrorCode}";
        }
    }

}
