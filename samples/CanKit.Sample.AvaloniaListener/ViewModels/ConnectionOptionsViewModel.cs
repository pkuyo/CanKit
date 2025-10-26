using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class ConnectionOptionsViewModel : ObservableObject
    {
        private readonly IConnectionOptionsContext _ctx;

        public ConnectionOptionsViewModel(IConnectionOptionsContext ctx)
        {
            _ctx = ctx;
            OpenFiltersCommand = new RelayCommand(_ => OnOpenFilters());
        }

        // Back-compat convenience
        public ConnectionOptionsViewModel(MainViewModel? main)
            : this(new ConnectionOptionsContext(main ?? new MainViewModel()))
        {
        }

        // Pass-through collections
        public ObservableCollection<int> BitRates => _ctx.BitRates;
        public ObservableCollection<int> DataBitRates => _ctx.DataBitRates;
        public ObservableCollection<FilterRuleModel> Filters => _ctx.Filters;

        // Capability flags
        public bool SupportsCan20 => _ctx.SupportsCan20;
        public bool SupportsCanFd => _ctx.SupportsCanFd;
        public bool SupportsListenOnly => _ctx.SupportsListenOnly;
        public bool SupportsErrorCounters => _ctx.SupportsErrorCounters;

        // Selected values
        public bool UseCan20
        {
            get => _ctx.UseCan20;
            set
            {
                if (_ctx.UseCan20 != value)
                {
                    _ctx.UseCan20 = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseCanFd));
                }
            }
        }

        public bool UseCanFd
        {
            get => _ctx.UseCanFd;
            set
            {
                if (_ctx.UseCanFd != value)
                {
                    _ctx.UseCanFd = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseCan20));
                }
            }
        }

        public int? SelectedBitRate
        {
            get => _ctx.SelectedBitRate;
            set
            {
                if (_ctx.SelectedBitRate != value)
                {
                    _ctx.SelectedBitRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? SelectedDataBitRate
        {
            get => _ctx.SelectedDataBitRate;
            set
            {
                if (_ctx.SelectedDataBitRate != value)
                {
                    _ctx.SelectedDataBitRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ListenOnly
        {
            get => _ctx.ListenOnly;
            set
            {
                if (_ctx.ListenOnly != value)
                {
                    _ctx.ListenOnly = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ErrorCountersPeriodMs
        {
            get => _ctx.ErrorCountersPeriodMs;
            set
            {
                if (_ctx.ErrorCountersPeriodMs != value)
                {
                    _ctx.ErrorCountersPeriodMs = value;
                    OnPropertyChanged();
                }
            }
        }

        public RelayCommand OpenFiltersCommand { get; }

        private void OnOpenFilters()
        {
            Dispatcher.UIThread.Post(async void () =>
            {
                try
                {
                    var dlg = new Views.FilterEditorWindow(Filters);
                    var app = Application.Current;
                    var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    await dlg.ShowDialog<bool?>(owner!);
                }
                catch
                {
                    // ignore
                }
            });
        }
    }
}
