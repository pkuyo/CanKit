using System;
using System.Text;

namespace Pkuyo.CanKit.Net.Core.Definitions
{
    public abstract record CanFrameBase
    {
        public abstract CanFrameType FrameKind { get; }

    }
    
    public record CanClassicFrame : CanFrameBase
    {
        public CanClassicFrame(uint rawID, byte[] data) 
        {
            RawID = rawID;
            _data = data;
        }

        public CanClassicFrame(uint ID, byte[] data, bool extendedFrame = false,
            bool remoteFrame = false,
            bool errorFrame = false) 
        {
            RawID = ID;
            IsExtendedFrame = extendedFrame;
            IsRemoteFrame = remoteFrame;
            IsErrorFrame = errorFrame;
            _data = data ?? [];
        }

        public uint RawID { get; set; }

        public uint ID
        {
            get => RawID & 0x1FFFFFFF;
            set => RawID = (uint)((RawID & ~0x1FFFFFFF) | (value & 0x1FFFFFFF));
        }

        public virtual byte Dlc => (byte)DataLen;

        public bool IsExtendedFrame
        {
            get => (RawID & (1 << 31)) != 0;
            set
            {
                if (value)
                    RawID |= (1u << 31);
                else
                    RawID &= ~(1u << 31);
            }
        }
        public bool IsRemoteFrame
        {
            get => (RawID & (1 << 30)) != 0;
            set
            {
                if (value)
                    RawID |= (1u << 30);
                else
                    RawID &= ~(1u << 30);
            }
        }

        public bool IsErrorFrame
        {
            get => (RawID & (1 << 29)) != 0;
            set
            {
                if (value)
                    RawID |= (1u << 29);
                else
                    RawID &= ~(1u << 29);
            }
        }

        public virtual byte[] Data
        {
            get => _data;
            set
            {
                if (value == null)
                {
                    _data =[];
                    return;
                }
                if (value.Length > 8)
                    throw new ArgumentOutOfRangeException(nameof(value), "Classic CAN frame data length cannot exceed 8 bytes.");
                _data = value;
            }
        }
        public string GetStringData(string encoding)
        {
            Encoding enc = Encoding.GetEncoding(encoding);
            return enc.GetString(Data);
        }

      
        
   

     

        public virtual int DataLen => _data?.Length ?? 0;

        public override CanFrameType FrameKind => CanFrameType.CanClassic;


        protected byte[] _data = null;
    }

    
 
    public record CanFdFrame : CanClassicFrame
    {
        public CanFdFrame(byte flags, uint rawID, byte[] data) : base(rawID, data) 
        {
            BRS = (flags & 0x1) != 0;
            ESI = (flags & 0x2) != 0;
        }

        public override byte[] Data
        {
            get => _data;
            set
            {
                if (value == null)
                {
                    _data = value;
                    return;
                }
                _ = LenToDlc(_data.Length);
                _data = value;
            }
        }

        public override byte Dlc => LenToDlc(DataLen);

        public static int DlcToLen(byte dlc)
        {
            if (dlc <= 8) return dlc;
            return dlc switch
            {
                9 => 12,
                10 => 16,
                11 => 20,
                12 => 24,
                13 => 32,
                14 => 48,
                15 => 64,
                _ => throw new ArgumentOutOfRangeException(nameof(dlc))
            };
        }

        public static byte LenToDlc(int len)
        {
            if (len < 0 || len > 64) throw new ArgumentOutOfRangeException(nameof(len));
            if (len <= 8) return (byte)len;
            return len switch
            {
                <= 12 => 9,
                <= 16 => 10,
                <= 20 => 11,
                <= 24 => 12,
                <= 32 => 13,
                <= 48 => 14,
                _ => 15,
            };
        }

        public byte Flag => (byte)((BRS ? 1 : 0) + (ESI ? 2 : 0));

        public bool BRS { get; set; } 

        public bool ESI { get; set; }


        public override CanFrameType FrameKind => CanFrameType.CanFd;
    }
    
    

}
