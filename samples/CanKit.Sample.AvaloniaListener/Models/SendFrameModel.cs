using System;
using System.Linq;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Models
{
    public class SendListItem : ObservableObject
    {
        private bool _isEnabled;
        private DeviceSessionViewModel? _selectedDevice;
        private ICanFrame _frame;
        private int _delayMs;

        public SendListItem(ICanFrame frame, DeviceSessionViewModel? device)
        {
            _frame = frame;
            _selectedDevice = device;
            _isEnabled = true;
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public DeviceSessionViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    OnPropertyChanged(nameof(DeviceName));
                }
            }
        }

        public string DeviceName => SelectedDevice?.DisplayName ?? "(未选择)";

        public ICanFrame Frame
        {
            get => _frame;
            set
            {
                if (SetProperty(ref _frame, value))
                {
                    // notify dependent fields
                    OnPropertyChanged(nameof(Kind));
                    OnPropertyChanged(nameof(IdText));
                    OnPropertyChanged(nameof(Dlc));
                    OnPropertyChanged(nameof(IsRemote));
                    OnPropertyChanged(nameof(IsBRS));
                    OnPropertyChanged(nameof(DataText));
                }
            }
        }

        public int DelayMs
        {
            get => _delayMs;
            set => SetProperty(ref _delayMs, Math.Max(0, value));
        }

        public string Kind => Frame.FrameKind == CanFrameType.CanFd ? "FD" : "2.0";
        public string IdText => Frame.IsExtendedFrame ? $"0x{Frame.ID:X8}" : $"0x{Frame.ID:X3}";
        public int Dlc => Frame.Dlc;
        public bool IsRemote => Frame is CanClassicFrame c && c.IsRemoteFrame;
        public bool IsBRS => Frame is CanFdFrame f && f.BitRateSwitch;

        public string Flag => IsRemote ? "Remote" : IsBRS ? "BRS" : "_";
        public string DataText => ToHex(Frame.Data.Span);

        private static string ToHex(ReadOnlySpan<byte> span)
        {
            if (span.Length == 0) return string.Empty;
            char[] buffer = new char[span.Length * 3 - 1];
            int pos = 0;
            for (int i = 0; i < span.Length; i++)
            {
                byte b = span[i];
                buffer[pos++] = GetHexChar(b >> 4);
                buffer[pos++] = GetHexChar(b & 0xF);
                if (i < span.Length - 1) buffer[pos++] = ' ';
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
