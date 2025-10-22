using System;
using System.Linq;
using CanKit.Core.Definitions;

namespace EndpointListenerWpf.Models
{
    public class FrameRow
    {
        public string Time { get; }
        public string Dir { get; }
        public string Kind { get; }
        public string Id { get; }
        public int Dlc { get; }
        public string Data { get; }

        private FrameRow(string time, string dir, string kind, string id, int dlc, string data)
        {
            Time = time;
            Dir = dir;
            Kind = kind;
            Id = id;
            Dlc = dlc;
            Data = data;
        }

        public static FrameRow From(ICanFrame f, FrameDirection dir)
        {
            var time = DateTime.Now.ToString("HH:mm:ss.fff");
            var d = dir == FrameDirection.Tx ? "Tx" : "Rx";
            var kind = f.FrameKind == CanFrameType.CanFd ? "FD" : "2.0";
            var idHex = f.IsExtendedFrame ? $"0x{f.ID:X8}" : $"0x{f.ID:X3}";

            var span = f.Data.Span;
            string data = string.Empty;
            if (span.Length > 0)
            {
                var hex = Convert.ToHexString(span).ToLowerInvariant();
                data = string.Join(" ", Enumerable.Range(0, hex.Length / 2)
                    .Select(i => hex.Substring(i * 2, 2)));
            }
            return new FrameRow(time, d, kind, idHex, f.Dlc, data);
        }
    }
}

