using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CanKit.Core.Definitions;

namespace EndpointListenerWpf.Models
{
    public class FrameRow : INotifyPropertyChanged
    {
        private string _time = string.Empty;
        private string _dir = string.Empty;
        private string _kind = string.Empty;
        private string _id = string.Empty;
        private int _dlc;
        private string _data = string.Empty;
        private int _count;
        private string _period = "—";
        private double _avgPeriodMs;
        private int _periodSamples; // number of intervals accumulated (capped at 1000)
        private DateTime _lastSeen;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Time { get => _time; private set { if (_time != value) { _time = value; OnPropertyChanged(nameof(Time)); } } }
        public string Dir { get => _dir; private set { if (_dir != value) { _dir = value; OnPropertyChanged(nameof(Dir)); } } }
        public string Kind { get => _kind; private set { if (_kind != value) { _kind = value; OnPropertyChanged(nameof(Kind)); } } }
        public string Id { get => _id; private set { if (_id != value) { _id = value; OnPropertyChanged(nameof(Id)); } } }
        public int Dlc { get => _dlc; private set { if (_dlc != value) { _dlc = value; OnPropertyChanged(nameof(Dlc)); } } }
        public string Data { get => _data; private set { if (_data != value) { _data = value; OnPropertyChanged(nameof(Data)); } } }
        public int Count { get => _count; private set { if (_count != value) { _count = value; OnPropertyChanged(nameof(Count)); } } }
        public string Period { get => _period; private set { if (_period != value) { _period = value; OnPropertyChanged(nameof(Period)); } } }

        private FrameRow()
        {
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static FrameRow From(CanReceiveData rec, FrameDirection dir)
        {
            var row = new FrameRow();
            row.UpdateFrom(rec, dir);
            return row;
        }

        public void UpdateFrom(CanReceiveData rec, FrameDirection dir)
        {
            var now = DateTime.Now;
            var f = rec.CanFrame;
            // Compute basic fields
            // if don't have rec time, use system time
            if (rec.ReceiveTimestamp.TotalMilliseconds == 0)
            {
                Time = now.ToString("HH:mm:ss");
            }
            else
            {
                Time = rec.ReceiveTimestamp.TotalMilliseconds.ToString($"G4",CultureInfo.InvariantCulture);
            }

            Dir = dir == FrameDirection.Tx ? "Tx" : "Rx";
            Kind = f.FrameKind == CanFrameType.CanFd ? "FD" : "2.0";
            Id = f.IsExtendedFrame ? $"0x{f.ID:X8}" : $"0x{f.ID:X3}";
            Dlc = f.Dlc;

            var span = f.Data.Span;
            if (span.Length > 0)
            {
                var hex = Convert.ToHexString(span).ToLowerInvariant();
                Data = string.Join(" ", Enumerable.Range(0, hex.Length / 2)
                    .Select(i => hex.Substring(i * 2, 2)));
            }
            else
            {
                Data = string.Empty;
            }
            if (_lastSeen == default)
            {
                Period = "—";
            }
            else
            {
                var ms = (now - _lastSeen).TotalMilliseconds;
                int n = Math.Min(_periodSamples, 1000);
                _avgPeriodMs = ((_avgPeriodMs * n) + ms) / (n + 1);
                if (_periodSamples < 1000) _periodSamples++;
                Period = $"{_avgPeriodMs:0.###} ms";
            }
            _lastSeen = now;
            Count++;
        }
    }
}
