using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using CanKit.Core.Definitions;
using CanKit.Protocol.IsoTp;

namespace CanKit.Sample.IsoTpDemo
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Usage:
            //  IsoTpDemo --ep "virtual://alpha/0" --txid 0x7E0 --rxid 0x7E8 [--fd] [--brs]
            //            [--baud 500000 | (--abit 500000 --dbit 2000000)] [--ext]
            //            [--ea 0x00] [--pad 1] [--req 22F190] [--timeout 2000]

            var ep = GetArg(args, "--ep") ?? "virtual://alpha/0";
            var txid = ParseInt(GetArg(args, "--txid") ?? "0x7E0");
            var rxid = ParseInt(GetArg(args, "--rxid") ?? "0x7E8");
            bool useFd = HasFlag(args, "--fd");
            bool brs = HasFlag(args, "--brs");
            bool ext = HasFlag(args, "--ext");
            int baud = ParseInt(GetArg(args, "--baud") ?? "500000");
            int abit = ParseInt(GetArg(args, "--abit") ?? "500000");
            int dbit = ParseInt(GetArg(args, "--dbit") ?? "2000000");
            byte? ea = TryParseByte(GetArg(args, "--ea"));
            bool pad = ParseInt(GetArg(args, "--pad") ?? "1") == 1;
            int timeout = ParseInt(GetArg(args, "--timeout") ?? "2000");
            var reqHex = GetArg(args, "--req") ?? "22F190"; // UDS ReadDataByIdentifier
            var req = HexToBytes(reqHex);

            var settings = new IsoTpSettings
            {
                TxId = txid,
                RxId = rxid,
                IsExtendedId = ext,
                ExtendedAddress = ea,
                PadFrames = pad,
                PadByte = 0x00,
                UseFd = useFd,
                FdBitRateSwitch = brs,
                FdDlc = 64,
                N_As = 1000,
                N_Bs = 1000,
                N_Cr = 1000,
            };

            Console.WriteLine($"Opening: {ep} (mode={(useFd ? "FD" : "Classic")}) ...");
            using var link = IsoTpLink.Open(ep, settings, conf =>
            {
                if (useFd)
                    conf.Fd(abit, dbit).SetProtocolMode(CanProtocolMode.CanFd);
                else
                    conf.Baud(baud).SetProtocolMode(CanProtocolMode.Can20);
                // narrow filter for Rx ID
                var idType = ext ? CanFilterIDType.Extend : CanFilterIDType.Standard;
                conf.RangeFilter(settings.RxId, settings.RxId, idType);
            });

            Console.WriteLine($"TX ID=0x{txid:X}, RX ID=0x{rxid:X}, ext={(ext ? 1:0)}, ea={(ea.HasValue ? "0x"+ea.Value.ToString("X2") : "n/a")}");
            Console.WriteLine($"Sending request: {ToHex(req)}");

            var resp = await link.RequestAsync(req, timeout);
            Console.WriteLine($"Response ({resp.Length}B): {ToHex(resp)}");
            return 0;
        }

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
        private static bool HasFlag(string[] args, string name)
        {
            foreach (var a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static int ParseInt(string s)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(s, CultureInfo.InvariantCulture);
        }
        private static byte? TryParseByte(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var v = ParseInt(s);
            if (v < 0 || v > 255) return null;
            return (byte)v;
        }
        private static int ParseInt(string? s, int def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            try { return ParseInt(s!); } catch { return def; }
        }
        private static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", string.Empty);
            if ((hex.Length & 1) != 0) hex = "0" + hex;
            var buf = new byte[hex.Length / 2];
            for (int i = 0; i < buf.Length; i++)
                buf[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return buf;
        }
        private static string ToHex(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return string.Empty;
            var arr = ArrayPool<char>.Shared.Rent(span.Length * 2);
            try
            {
                for (int i = 0; i < span.Length; i++)
                {
                    var b = span[i];
                    arr[2 * i] = GetHex((byte)(b >> 4));
                    arr[2 * i + 1] = GetHex((byte)(b & 0xF));
                }
                return new string(arr, 0, span.Length * 2);
            }
            finally { ArrayPool<char>.Shared.Return(arr); }
        }
        private static char GetHex(byte v) => (char)(v < 10 ? ('0' + v) : ('A' + (v - 10)));
    }
}
