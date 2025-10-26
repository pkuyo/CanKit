using System;
using CanKit.Core.Definitions;
using System.Linq;
using CanKit.Sample.AvaloniaListener.Abstractions;

namespace CanKit.Sample.AvaloniaListener.Models
{
    public class PeriodicItemModel : ObservableObject
    {
        private bool _enabled = true;
        private int _id;
        private int _periodMs;
        private bool _isFd;
        private bool _isExtended;
        private bool _isRemote;
        private bool _brs;
        private byte[] _dataBytes = [];
        private byte _dlc;

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public int Id
        {
            get => _id;
            set
            {
                if (SetProperty(ref _id, value))
                {
                    OnPropertyChanged(nameof(Display));
                }
            }
        }

        public int PeriodMs
        {
            get => _periodMs;
            set
            {
                if (SetProperty(ref _periodMs, value))
                    OnPropertyChanged(nameof(PeriodDisplay));
            }
        }

        public bool IsFd
        {
            get => _isFd;
            set
            {
                if (SetProperty(ref _isFd, value))
                {
                    OnPropertyChanged(nameof(Display));
                    OnPropertyChanged(nameof(FlagsDisplay));
                }
            }
        }

        public bool IsExtended
        {
            get => _isExtended;
            set
            {
                if (SetProperty(ref _isExtended, value))
                {
                    OnPropertyChanged(nameof(Display));
                }
            }
        }

        public bool IsRemote
        {
            get => _isRemote;
            set
            {
                if (SetProperty(ref _isRemote, value))
                {
                    OnPropertyChanged(nameof(FlagsDisplay));
                }
            }
        }

        public bool Brs
        {
            get => _brs;
            set
            {
                if (SetProperty(ref _brs, value))
                {
                    OnPropertyChanged(nameof(FlagsDisplay));
                }
            }
        }

        public byte[] DataBytes
        {
            get => _dataBytes;
            set
            {
                if (SetProperty(ref _dataBytes, value))
                {
                    OnPropertyChanged(nameof(DataDisplay));
                }
            }
        }

        public byte Dlc
        {
            get => _dlc;
            set
            {
                if (SetProperty(ref _dlc, value))
                {
                    OnPropertyChanged(nameof(Display));
                }
            }
        }

        public string Display
            => $"{(IsFd ? "FD" : "CAN")} {(IsExtended ? "Ext" : "Std")} 0x{Id:X3}";
        public string PeriodDisplay => $"{PeriodMs} ms";
        public string FlagsDisplay => IsFd ? (Brs ? "BRS" : string.Empty) : (IsRemote ? "RTR" : string.Empty);
        public string DataDisplay => DataBytes is { Length: > 0 } ? string.Join(" ", DataBytes.Select(b => b.ToString("X2"))) : string.Empty;

        public (ICanFrame frame, TimeSpan period) ToSchedule()
        {
            var period = TimeSpan.FromMilliseconds(Math.Max(1, PeriodMs));
            if (IsFd)
            {
                var fd = new CanFdFrame(Id, DataBytes, BRS: Brs, ESI: false, isExtendedFrame: IsExtended);
                return (fd, period);
            }
            else
            {
                var classic = new CanClassicFrame(Id, DataBytes, isExtendedFrame: IsExtended, isRemoteFrame: IsRemote);
                return (classic, period);
            }
        }
    }
}

