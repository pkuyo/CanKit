using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ZlgCAN.Net.Core.Utils;
using static ZlgCAN.Net.Native.ZLGCAN;

namespace ZlgCAN.Net.Core.Models
{
    public abstract record CanFrameBase
    {

        public abstract CanFrameFlag FrameKind { get; }

        public virtual ZCANDataObj ToZCANObj(byte channelID)
        {
            ZCANDataObj obj = new ZCANDataObj
            {
                dataType = FrameType,
                chnl = channelID
            };
            return obj;
        }

        internal byte FrameType => ZlgNativeExtension.GetFrameType(FrameKind);

    }

    public record ClassicCanFrame : CanFrameBase
    {
        public ClassicCanFrame(uint rawID, byte[] data) 
        {
            RawID = rawID;
            _data = data;
        }

        public ClassicCanFrame(uint ID, byte[] data, bool extendedFrame = false,
            bool remoteFrame = false,
            bool errorFrame = false) 
        {
            RawID = ID;
            IsExtendedFrame = extendedFrame;
            IsRemoteFrame = remoteFrame;
            IsErrorFrame = errorFrame;
            _data = data ?? Array.Empty<byte>();
        }

        public uint RawID { get; set; }

        public uint ID
        {
            get => RawID & 0x1FFFFFFF;
            set => RawID = (uint)((RawID & ~0x1FFFFFFF) | (value & 0x1FFFFFFF));
        }

        public virtual uint Dlc => (uint)DataLen;

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
                    _data = value;
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

        public unsafe override ZCANDataObj ToZCANObj(byte channelID)
        {
            var obj = base.ToZCANObj(channelID);
            fixed (byte * ptr = _data)
            {
                var data = new ZCANCANFDData()
                {
                    frameType = 0,
                    timeStamp = 0,
                    frame = new canfd_frame()
                    {
                        can_id = RawID,
                        len = (byte)DataLen,
                    }
                };
                Unsafe.CopyBlockUnaligned(ptr, data.frame.data, (uint)DataLen);
                ZlgNativeExtension.StructCopyToBuffer(data, obj.data, 92);

            }
        
            return obj;
        }

        public virtual int DataLen => _data?.Length ?? 0;

        public override CanFrameFlag FrameKind => CanFrameFlag.ClassicCan;


        protected byte[] _data = null;
    }

    public record FdCanFrame : ClassicCanFrame
    {
        public FdCanFrame(byte flags, uint rawID, byte[] data) : base(rawID, data) 
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

        public override uint Dlc => LenToDlc(DataLen);

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
        public override ZCANDataObj ToZCANObj(byte channelID)
        {
            var obj = base.ToZCANObj(channelID);
            obj.flag = Flag;
            return obj;
        }

        public byte Flag => (byte)((BRS ? 1 : 0) + (ESI ? 2 : 0));

        public bool BRS { get; set; } 

        public bool ESI { get; set; }


        public override CanFrameFlag FrameKind => CanFrameFlag.CanFd;
    }

}
