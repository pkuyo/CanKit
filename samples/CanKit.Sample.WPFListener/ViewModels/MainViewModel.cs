using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EndpointListenerWpf.Models;
using EndpointListenerWpf.Services;

namespace EndpointListenerWpf.ViewModels
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
        private int _selectedBitRate;
        private int _selectedDataBitRate;
        private CancellationTokenSource? _listenerCts;

        // Filters
        public ObservableCollection<FilterRuleModel> Filters { get; } = new();

        public ObservableCollection<EndpointInfo> Endpoints { get; } = new();
        public ObservableCollection<int> BitRates { get; } = new();
        public ObservableCollection<int> DataBitRates { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

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
                    BitRates.Clear();
                    DataBitRates.Clear();
                    if (value != null)
                    {
                        foreach (var b in value.SupportedBitRates)
                            BitRates.Add(b);
                        foreach (var b in value.SupportedDataBitRates)
                            DataBitRates.Add(b);
                        SelectedBitRate = BitRates.FirstOrDefault();
                        SelectedDataBitRate = DataBitRates.FirstOrDefault();
                        UseCan20 = value.SupportsCan20;
                        UseCanFd = value.SupportsCanFd && !UseCan20 ? true : false;
                    }

                    UpdateCommandStates();
                }
            }
        }

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
                }
            }
        }

        public int SelectedBitRate
        {
            get => _selectedBitRate;
            set => SetProperty(ref _selectedBitRate, value);
        }

        public int SelectedDataBitRate
        {
            get => _selectedDataBitRate;
            set => SetProperty(ref _selectedDataBitRate, value);
        }

        public string CurrentEndpoint => IsCustomSelected ? CustomEndpoint.Trim() : SelectedEndpoint?.Id ?? string.Empty;

        public RelayCommand RefreshEndpointsCommand { get; }
        public RelayCommand OpenCustomEndpointCommand { get; }
        public RelayCommand StartListeningCommand { get; }
        public RelayCommand StopListeningCommand { get; }
        public RelayCommand OpenFilterEditorCommand { get; }

        public MainViewModel()
            : this(new CanKitEndpointDiscoveryService(), new CanKitDeviceService(), new CanKitListenerService())
        {
        }

        public MainViewModel(IEndpointDiscoveryService discoveryService, IDeviceService deviceService, IListenerService listenerService)
        {
            _discoveryService = discoveryService;
            _deviceService = deviceService;
            _listenerService = listenerService;

            RefreshEndpointsCommand = new RelayCommand(_ => _ = RefreshEndpointsAsync(), _ => !IsFetching && !IsListening);
            OpenCustomEndpointCommand = new RelayCommand(_ => _ = FetchCapabilitiesAsync(CurrentEndpoint), _ => !IsFetching);
            StartListeningCommand = new RelayCommand(_ => _ = StartListeningAsync(), _ => !IsListening);
            StopListeningCommand = new RelayCommand(_ => StopListening(), _ => IsListening);
            OpenFilterEditorCommand = new RelayCommand(_ => OpenFilterEditor());

            _ = RefreshEndpointsAsync();
        }

        private void UpdateCommandStates()
        {
            RefreshEndpointsCommand.RaiseCanExecuteChanged();
            OpenCustomEndpointCommand.RaiseCanExecuteChanged();
            StartListeningCommand.RaiseCanExecuteChanged();
            StopListeningCommand.RaiseCanExecuteChanged();
            OpenFilterEditorCommand.RaiseCanExecuteChanged();
        }

        private async Task RefreshEndpointsAsync()
        {
            try
            {
                IsFetching = true;
                Endpoints.Clear();
                var list = await _discoveryService.DiscoverAsync().ConfigureAwait(false);

                Application.Current.Dispatcher.Invoke(() =>
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
                Application.Current.Dispatcher.Invoke(() => Capabilities = caps);
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
                return;

            StopListening();
            _listenerCts = new CancellationTokenSource();
            IsListening = true;

            try
            {
                await Task.Run(async () =>
                {
                    try
                    {
                        await _listenerService.StartAsync(CurrentEndpoint, UseCan20, SelectedBitRate,
                                SelectedDataBitRate, Filters,
                                msg => { Application.Current.Dispatcher.Invoke(() => Logs.Add(msg)); },
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
                Application.Current.Dispatcher.Invoke(() => Logs.Add($"[exception] open '{CurrentEndpoint}' failed, exception: {e.Message}"));
            }
            finally
            {
                IsListening = false;
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

        private void OpenFilterEditor()
        {
            var win = new Views.FilterEditorWindow
            {
                Owner = Application.Current?.MainWindow
            };
            win.DataContext = new FilterEditorViewModel(Filters);
            win.ShowDialog();
        }
    }
}
