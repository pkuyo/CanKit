using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.Services;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IEndpointDiscoveryService _discoveryService;
        private readonly IDeviceService _deviceService;
        private readonly IListenerService _listenerService;

        private EndpointInfo? _selectedEndpoint;
        private string _customEndpoint = string.Empty;
        private DeviceCapabilities? _capabilities;
        private bool _isFetching;
        private bool _isListening;
        private bool _useCan20 = true;
        private bool _useCanFd = false;
        private bool _listenOnly = false;
        private int? _selectedBitRate;
        private int? _selectedDataBitRate;
        private int _tec;
        private int _rec;
        private float _busUsage;
        private int _errorCountersPeriodMs = 5000; // default 5s
        private CancellationTokenSource? _listenerCts;
        private readonly AppBusState _busState = new();
        private readonly IPeriodicTxService _periodicService;
        private readonly PeriodicViewModel _periodicVm;
        public ObservableCollection<FilterRuleModel> Filters { get; } = new();

        public ObservableCollection<EndpointInfo> Endpoints { get; } = new();
        public ObservableCollection<int> BitRates { get; set; } = new();
        public ObservableCollection<int> DataBitRates { get; set; } = new();
        public ObservableCollection<string> Logs { get; } = new();
        public FixedSizeObservableCollection<FrameRow> Frames { get; } = new(10000);

        public PeriodicViewModel Periodic => _periodicVm;

        public EndpointInfo? SelectedEndpoint
        {
            get => _selectedEndpoint;
            set
            {
                if (SetProperty(ref _selectedEndpoint, value))
                {
                    OnPropertyChanged(nameof(IsCustomSelected));
                    if (!IsCustomSelected && value != null)
                    {
                        _ = FetchCapabilitiesAsync(CurrentEndpoint);
                    }
                }
            }
        }

        public bool IsCustomSelected => SelectedEndpoint?.IsCustom == true;

        public string CustomEndpoint
        {
            get => _customEndpoint;
            set => SetProperty(ref _customEndpoint, value);
        }

        public DeviceCapabilities? Capabilities
        {
            get => _capabilities;
            private set
            {
                if (SetProperty(ref _capabilities, value))
                {
                    if (value != null)
                    {
                        BitRates.Clear();
                        DataBitRates.Clear();
                        foreach (var b in value.SupportedBitRates)
                            BitRates.Add(b);
                        foreach (var b in value.SupportedDataBitRates)
                            DataBitRates.Add(b);
                        SelectedBitRate = value.SupportedBitRates.FirstOrDefault();
                        SelectedDataBitRate = value.SupportedDataBitRates.FirstOrDefault();
                        UseCan20 = value.SupportsCan20;
                        UseCanFd = value.SupportsCanFd && !UseCan20 ? true : false;
                    }

                    OnPropertyChanged(nameof(SupportsListenOnly));
                    OnPropertyChanged(nameof(SupportsErrorCounters));
                    UpdateCommandStates();
                }
            }
        }
        public bool SupportsCanFD => Capabilities?.SupportsListenOnly == true;
        public bool SupportsListenOnly => Capabilities?.SupportsListenOnly == true;
        public bool SupportsErrorCounters => Capabilities?.SupportsErrorCounters == true;

        public bool SupportsBusUsage => Capabilities?.SupportsBusUsage == true;

        public bool IsFetching
        {
            get => _isFetching;
            private set
            {
                if (SetProperty(ref _isFetching, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public bool IsListening
        {
            get => _isListening;
            private set
            {
                if (SetProperty(ref _isListening, value))
                {
                    UpdateCommandStates();
                    _busState.SetListening(value);
                    _periodicVm.RefreshCanRun();
                }
            }
        }

        public bool UseCan20
        {
            get => _useCan20;
            set
            {
                if (value != _useCan20)
                {
                    SetProperty(ref _useCan20, value);
                    UseCanFd = !value;
                }
            }
        }

        public bool UseCanFd
        {
            get => _useCanFd;
            set
            {
                if (value != _useCanFd)
                {
                    SetProperty(ref _useCanFd, value);
                    UseCan20 = !value;
                    _periodicVm.AllowFd = value;
                }
            }
        }

        public bool ListenOnly
        {
            get => _listenOnly;
            set => SetProperty(ref _listenOnly, value);
        }

        public int? SelectedBitRate
        {
            get => _selectedBitRate;
            set => SetProperty(ref _selectedBitRate, value);
        }

        public int? SelectedDataBitRate
        {
            get => _selectedDataBitRate;
            set => SetProperty(ref _selectedDataBitRate, value);
        }

        public int Tec
        {
            get => _tec;
            private set => SetProperty(ref _tec, value);
        }

        public int Rec
        {
            get => _rec;
            private set => SetProperty(ref _rec, value);
        }

        public float BusUsage
        {
            get => _busUsage;
            set => SetProperty(ref _busUsage, value);
        }

        public int ErrorCountersPeriodMs
        {
            get => _errorCountersPeriodMs;
            set => SetProperty(ref _errorCountersPeriodMs, Math.Max(100, value));
        }

        public string CurrentEndpoint => IsCustomSelected ? CustomEndpoint.Trim() : SelectedEndpoint?.Id ?? string.Empty;

        public RelayCommand RefreshEndpointsCommand { get; }
        public RelayCommand OpenCustomEndpointCommand { get; }
        public RelayCommand StartListeningCommand { get; }
        public RelayCommand StopListeningCommand { get; }
        public RelayCommand CopyFrameToClipboardCommand { get; }

        public MainViewModel()
            : this(new CanKitEndpointDiscoveryService(), new CanKitDeviceService(), new CanKitListenerService())
        {
        }

        public MainViewModel(IEndpointDiscoveryService discoveryService, IDeviceService deviceService, IListenerService listenerService)
        {
            _discoveryService = discoveryService;
            _deviceService = deviceService;
            _listenerService = listenerService;
            _periodicService = new PeriodicTxService(_listenerService);
            _periodicVm = new PeriodicViewModel(_busState, _periodicService);

            RefreshEndpointsCommand = new RelayCommand(_ => _ = RefreshEndpointsAsync(), _ => !IsFetching && !IsListening);
            OpenCustomEndpointCommand = new RelayCommand(_ => _ = FetchCapabilitiesAsync(CurrentEndpoint), _ => !IsFetching);
            StartListeningCommand = new RelayCommand(_ => _ = StartListeningAsync(), _ => !IsListening);
            StopListeningCommand = new RelayCommand(_ => StopListening(), _ => IsListening);
            CopyFrameToClipboardCommand = new RelayCommand(p =>
            {
                if (p is FrameRow row)
                {
                    CopyFrameToClipboard(row);
                }
            }, p => p is FrameRow);

            _ = RefreshEndpointsAsync();
        }

        public int Transmit(ICanFrame frame) => _listenerService.Transmit(frame);

        private void UpdateCommandStates()
        {
            RefreshEndpointsCommand.RaiseCanExecuteChanged();
            OpenCustomEndpointCommand.RaiseCanExecuteChanged();
            StartListeningCommand.RaiseCanExecuteChanged();
            StopListeningCommand.RaiseCanExecuteChanged();
        }

        private async Task RefreshEndpointsAsync()
        {
            try
            {
                IsFetching = true;
                Endpoints.Clear();
                var list = await _discoveryService.DiscoverAsync().ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var ep in list)
                        Endpoints.Add(ep);
                    SelectedEndpoint = Endpoints.FirstOrDefault();
                });
                Logs.Add("[info] Endpoints refreshed.");
            }
            catch (Exception ex)
            {
                Logs.Add("[error] Failed to refresh endpoints: " + ex.Message);
            }
            finally
            {
                IsFetching = false;
            }
        }

        private async Task FetchCapabilitiesAsync(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                Logs.Add("[warn] Endpoint is empty.");
                return;
            }

            try
            {
                IsFetching = true;
                Logs.Add($"[info] Querying capabilities for '{endpoint}'...");
                var caps = await _deviceService.GetCapabilitiesAsync(endpoint).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() => Capabilities = caps);
                Logs.Add("[info] Capabilities loaded.");
            }
            catch (Exception ex)
            {
                Logs.Add("[error] Failed to get capabilities: " + ex.Message);
            }
            finally
            {
                IsFetching = false;
            }
        }

        private async Task StartListeningAsync()
        {
            if (Capabilities == null)
            {
                Logs.Add("[info] Load capabilities first (select endpoint).");
                return;
            }

            StopListening();
            _listenerCts = new CancellationTokenSource();
            IsListening = true;

            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        await _listenerService.StartAsync(CurrentEndpoint, UseCan20, SelectedBitRate ?? 500_000,
                                SelectedDataBitRate ?? 5_000_000, Filters,
                                Capabilities!.Features,
                                (ListenOnly && Capabilities!.SupportsListenOnly),
                                ErrorCountersPeriodMs,
                                (f, d) =>
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        Frames.Add(FrameRow.From(f, d));
                                    });
                                },
                                msg => { Dispatcher.UIThread.Post(() => Logs.Add(msg)); },
                                counters =>
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        Tec = counters.TransmitErrorCounter;
                                        Rec = counters.ReceiveErrorCounter;
                                    });
                                },
                                busUsage =>
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        BusUsage = busUsage;
                                    });
                                },
                                _listenerCts!.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // normal on stop
                    }
                });
            }
            catch (Exception e)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Logs.Add($"[exception] open '{CurrentEndpoint}' failed, exception: {e.Message}"));
            }
            finally
            {
                IsListening = false;
                _periodicVm.IsRunning = false;
            }
        }

        private void StopListening()
        {
            if (_listenerCts != null)
            {
                _listenerCts.Cancel();
                _listenerCts.Dispose();
                _listenerCts = null;
            }
            IsListening = false;
        }

        private void CopyFrameToClipboard(FrameRow row)
        {
            try
            {
                var text = $"{row.Time} {row.Dir} {row.Kind} {row.Id} {row.Dlc} {row.Data}".TrimEnd();
                // Fire and forget
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        var app = Application.Current;
                        if (app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } w)
                        {
                            await w.Clipboard!.SetTextAsync(text);
                        }
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                Logs.Add($"[error] Copy failed: {ex.Message}");
            }
        }
    }
}
