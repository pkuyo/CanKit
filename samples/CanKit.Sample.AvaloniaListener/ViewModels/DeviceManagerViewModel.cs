using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.Services;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class DeviceManagerViewModel : ObservableObject
    {
        private readonly IEndpointDiscoveryService _discoveryService;
        private readonly IDeviceService _deviceService;
        private readonly MainViewModel _main;

        public ObservableCollection<EndpointInfo> Endpoints { get; } = new();
        public ObservableCollection<DeviceSessionViewModel> ConnectedDevices { get; }

        private EndpointInfo? _selectedEndpoint;
        public EndpointInfo? SelectedEndpoint
        {
            get => _selectedEndpoint;
            set
            {
                if (SetProperty(ref _selectedEndpoint, value))
                {
                    OnPropertyChanged(nameof(IsCustomSelected));
                    ConnectDeviceCommand.RaiseCanExecuteChanged();
                    ProbeCapabilitiesCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private DeviceSessionViewModel? _selectedDevice;
        public DeviceSessionViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                SetProperty(ref _selectedDevice, value);
                DisconnectDeviceCommand.RaiseCanExecuteChanged();
            }
        }

        // Custom endpoint support
        private string _customEndpoint = string.Empty;
        public string CustomEndpoint
        {
            get => _customEndpoint;
            set
            {
                if (SetProperty(ref _customEndpoint, value))
                {
                    ConnectDeviceCommand.RaiseCanExecuteChanged();
                    ProbeCapabilitiesCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsCustomSelected => SelectedEndpoint?.IsCustom == true;

        private string _probeResult = string.Empty;
        public string ProbeResult
        {
            get => _probeResult;
            private set => SetProperty(ref _probeResult, value);
        }

        private bool _isFetching;
        public bool IsFetching
        {
            get => _isFetching;
            private set
            {
                if (SetProperty(ref _isFetching, value))
                {
                    RefreshEndpointsCommand.RaiseCanExecuteChanged();
                    ConnectDeviceCommand.RaiseCanExecuteChanged();
                    DisconnectDeviceCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private readonly CancellationTokenSource _cts = new();

        public RelayCommand RefreshEndpointsCommand { get; }
        public RelayCommand ConnectDeviceCommand { get; }
        public RelayCommand DisconnectDeviceCommand { get; }
        public AsyncRelayCommand ProbeCapabilitiesCommand { get; }
        public AsyncRelayCommand GenerateEndpointCommand { get; }

        public DeviceManagerViewModel(MainViewModel main)
            : this(main, new CanKitEndpointDiscoveryService(), new CanKitCapabilityService()) { }

        public DeviceManagerViewModel(MainViewModel main, IEndpointDiscoveryService discovery, IDeviceService deviceService)
        {
            _main = main;
            _discoveryService = discovery;
            _deviceService = deviceService;
            ConnectedDevices = main.ConnectedDevices;

            RefreshEndpointsCommand = new RelayCommand(_ => _ = RefreshEndpointsAsync(), _ => !IsFetching);
            ConnectDeviceCommand = new RelayCommand(_ => _ = ConnectSelectedAsync(), _ => !IsFetching && CanConnect());
            DisconnectDeviceCommand = new RelayCommand(_ => DisconnectSelected(), _ => SelectedDevice != null);
            ProbeCapabilitiesCommand = new AsyncRelayCommand(async _ => await ProbeSelectedAsync(), _ => IsCustomSelected && !string.IsNullOrWhiteSpace(CustomEndpoint));
            GenerateEndpointCommand = new AsyncRelayCommand(async _ => await GenerateEndpointAsync());

            _ = RefreshEndpointsAsync();
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
            }
            finally
            {
                IsFetching = false;
            }
        }

        private async Task ConnectSelectedAsync()
        {
            var ep = SelectedEndpoint;
            if (ep == null) return;

            // Load caps
            var endpointId = ep.IsCustom ? CustomEndpoint : ep.Id;
            if (string.IsNullOrWhiteSpace(endpointId)) return;
            var caps = await _deviceService.GetCapabilitiesAsync(endpointId).ConfigureAwait(false);

            // Create a session VM
            var epInfo = ep.IsCustom
                ? new EndpointInfo { Id = endpointId, DisplayName = endpointId, IsCustom = true }
                : ep;

            var session = new DeviceSessionViewModel(
                epInfo,
                caps,
                listenerService: null,
                onFrameRow: row => _main.AddFrame(row),
                onLog: msg => _main.Logs.Add(msg));

            var vm = new ConnectionOptionsViewModel(new DeviceSessionConnectionOptionsContext(session));
            await ShowOptionsDialogAsync(vm);

            ConnectedDevices.Add(session);
            session.PropertyChanged += Session_PropertyChanged;
            _main.ApplyFrameFilter();
            _ = session.StartAsync(_cts.Token);
        }

        private void DisconnectSelected()
        {
            var s = SelectedDevice;
            if (s == null) return;
            s.PropertyChanged -= Session_PropertyChanged;
            s.Stop();
            s.Dispose();
            ConnectedDevices.Remove(s);
            _main.ApplyFrameFilter();
        }

        private void Session_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceSessionViewModel.IsSelectedForView))
            {
                _main.ApplyFrameFilter();
            }
        }

        private static async Task ShowOptionsDialogAsync(ConnectionOptionsViewModel vm)
        {
            // Use UI thread to show dialog with owner as MainWindow
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new Views.ConnectionOptionsWindow(vm);
                var app = Avalonia.Application.Current;
                var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                await dlg.ShowDialog<bool?>(owner!);
            });
        }

        private bool CanConnect()
        {
            if (IsFetching) return false;
            var ep = SelectedEndpoint;
            if (ep == null) return false;
            if (!ep.IsCustom) return true;
            return !string.IsNullOrWhiteSpace(CustomEndpoint);
        }

        private async Task ProbeSelectedAsync()
        {
            ProbeResult = string.Empty;
            var ep = SelectedEndpoint;
            if (ep == null || !ep.IsCustom) return;
            var id = CustomEndpoint;
            if (string.IsNullOrWhiteSpace(id)) return;
            try
            {
                var caps = await _deviceService.GetCapabilitiesAsync(id).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var modes = (caps.SupportsCan20 ? "CAN2.0 " : string.Empty) + (caps.SupportsCanFd ? "CAN-FD" : string.Empty);
                    ProbeResult = $"探测成功: {modes.Trim()}";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ProbeResult = $"探测失败: {ex.Message}");
            }
        }

        private static async Task<string?> ShowEndpointGeneratorDialogAsync()
        {
            var tcs = new TaskCompletionSource<string?>();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    var dlg = new Views.EndpointGeneratorDialog();
                    var app = Avalonia.Application.Current;
                    var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    var result = await dlg.ShowDialog<string?>(owner!);
                    tcs.SetResult(result);
                }
                catch
                {
                    tcs.SetResult(null);
                }
            });
            return await tcs.Task.ConfigureAwait(false);
        }

        private async Task GenerateEndpointAsync()
        {
            var ep = await ShowEndpointGeneratorDialogAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(ep)) return;
            CustomEndpoint = ep!;
            // auto probe
            await ProbeSelectedAsync().ConfigureAwait(false);
        }

        public void StopAll()
        {
            foreach (var s in ConnectedDevices.ToArray())
            {
                try { s.Stop(); } catch { /*Ignored*/ }
            }
        }
    }
}
