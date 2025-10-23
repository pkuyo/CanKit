using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class ConnectionOptionsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;

        public ConnectionOptionsViewModel(MainViewModel? main)
        {
            _main = main ?? new MainViewModel();
            OpenFiltersCommand = new RelayCommand(_ => OnOpenFilters());
        }

        // Pass-through collections
        public ObservableCollection<int> BitRates => _main.BitRates;
        public ObservableCollection<int> DataBitRates => _main.DataBitRates;
        public ObservableCollection<FilterRuleModel> Filters => _main.Filters;

        // Capability flags
        public bool SupportsCan20 => _main.Capabilities?.SupportsCan20 ?? true;
        public bool SupportsCanFd => _main.Capabilities?.SupportsCanFd ?? false;
        public bool SupportsListenOnly => _main.SupportsListenOnly;
        public bool SupportsErrorCounters => _main.SupportsErrorCounters;

        // Selected values
        public bool UseCan20
        {
            get => _main.UseCan20;
            set
            {
                if (_main.UseCan20 != value)
                {
                    _main.UseCan20 = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseCanFd));
                }
            }
        }

        public bool UseCanFd
        {
            get => _main.UseCanFd;
            set
            {
                if (_main.UseCanFd != value)
                {
                    _main.UseCanFd = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseCan20));
                }
            }
        }

        public int? SelectedBitRate
        {
            get => _main.SelectedBitRate;
            set
            {
                if (_main.SelectedBitRate != value)
                {
                    _main.SelectedBitRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? SelectedDataBitRate
        {
            get => _main.SelectedDataBitRate;
            set
            {
                if (_main.SelectedDataBitRate != value)
                {
                    _main.SelectedDataBitRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ListenOnly
        {
            get => _main.ListenOnly;
            set
            {
                if (_main.ListenOnly != value)
                {
                    _main.ListenOnly = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ErrorCountersPeriodMs
        {
            get => _main.ErrorCountersPeriodMs;
            set
            {
                if (_main.ErrorCountersPeriodMs != value)
                {
                    _main.ErrorCountersPeriodMs = value;
                    OnPropertyChanged();
                }
            }
        }

        public RelayCommand OpenFiltersCommand { get; }

        private void OnOpenFilters()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var dlg = new Views.FilterEditorWindow(Filters);
                    var app = Application.Current;
                    var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    if (owner != null)
                        await dlg.ShowDialog<bool?>(owner);
                    else
                        await dlg.ShowDialog<bool?>(null!);
                }
                catch
                {
                    // ignore
                }
            });
        }
    }
}

