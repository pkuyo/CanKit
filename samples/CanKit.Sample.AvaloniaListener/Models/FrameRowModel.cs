using System;
using System.Linq;
using CanKit.Core.Definitions;

namespace CanKit.Sample.AvaloniaListener.Models
{
    public class FrameRow
    {
        public string Time { get; }
        public string Dir { get; }
        public string Kind { get; }
        public string Id { get; }
        public int Dlc { get; }
        public string Data { get; }
        public string Source { get; }
        public string SourceId { get; }

        private FrameRow(string time, string dir, string kind, string id, int dlc, string data, string source = "", string sourceId = "")
        {
            Time = time;
            Dir = dir;
            Kind = kind;
            Id = id;
            Dlc = dlc;
            Data = data;
            Source = source;
            SourceId = sourceId;
        }

        public static FrameRow From(ICanFrame f, FrameDirection dir)
        {
            var time = DateTime.Now.ToString("HH:mm:ss.fff");
            var d = dir == FrameDirection.Tx ? "Tx" : "Rx";
            var kind = f.FrameKind == CanFrameType.CanFd ? "FD" : "2.0";
            var idHex = f.IsExtendedFrame ? $"0x{f.ID:X8}" : $"0x{f.ID:X3}";
            string data = HexString(f.Data.Span);
            return new FrameRow(time, d, kind, idHex, f.Dlc, data);
        }

        public static FrameRow From(ICanFrame f, FrameDirection dir, string sourceId, string sourceDisplay)
        {
            var time = DateTime.Now.ToString("HH:mm:ss.fff");
            var d = dir == FrameDirection.Tx ? "Tx" : "Rx";
            var kind = f.FrameKind == CanFrameType.CanFd ? "FD" : "2.0";
            var idHex = f.IsExtendedFrame ? $"0x{f.ID:X8}" : $"0x{f.ID:X3}";
            string data = HexString(f.Data.Span);
            return new FrameRow(time, d, kind, idHex, f.Dlc, data, sourceDisplay, sourceId);
        }

        private static string HexString(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0)
                return string.Empty;
            char[] buffer = new char[span.Length * 3 - 1];
            int pos = 0;
            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];
                buffer[pos++] = GetHexChar(b >> 4);
                buffer[pos++] = GetHexChar(b & 0xF);
                if (i < span.Length - 1)
                {
                    buffer[pos++] = ' ';
                }
            }
            return new string(buffer);
        }

        private static char GetHexChar(int value)
        {
            value &= 0xF;
            return (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));
        }
    }
}
