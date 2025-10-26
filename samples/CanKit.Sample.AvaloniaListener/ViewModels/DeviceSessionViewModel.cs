using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.Services;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class DeviceSessionViewModel : ObservableObject, IConnectionOptionsContext, IDisposable
    {
        private readonly IConnectionService _connectionService;
        private readonly Action<FrameRow> _onFrameRow; // aggregate to main
        private readonly Action<string> _onLog;        // aggregate logs

        public string EndpointId { get; }
        public string DisplayName { get; }

        // Capabilities
        public DeviceCapabilities Capabilities { get; }

        public ObservableCollection<int> BitRates { get; } = new();
        public ObservableCollection<int> DataBitRates { get; } = new();
        public ObservableCollection<FilterRuleModel> Filters { get; } = new();

        private bool _useCan20;
        private bool _useCanFd;
        private bool _listenOnly;
        private int? _selectedBitRate;
        private int? _selectedDataBitRate;
        private int _errorCountersPeriodMs = 5000;

        private CancellationTokenSource? _cts;
        private bool _isListening;
        private bool _isSelectedForView = true;

        public bool IsListening
        {
            get => _isListening;
            private set => SetProperty(ref _isListening, value);
        }

        public bool IsSelectedForView
        {
            get => _isSelectedForView;
            set => SetProperty(ref _isSelectedForView, value);
        }

        public DeviceSessionViewModel(EndpointInfo endpoint, DeviceCapabilities caps,
            IConnectionService? listenerService,
            Action<FrameRow> onFrameRow,
            Action<string> onLog)
        {
            EndpointId = endpoint.Id;
            DisplayName = endpoint.DisplayName;
            Capabilities = caps;
            _connectionService = listenerService ?? new CanKitConnectionService();
            _onFrameRow = onFrameRow;
            _onLog = onLog;
            foreach (var b in caps.SupportedBitRates) BitRates.Add(b);
            foreach (var b in caps.SupportedDataBitRates) DataBitRates.Add(b);

            _useCan20 = caps.SupportsCan20;
            _useCanFd = caps.SupportsCanFd && !caps.SupportsCan20;
            _selectedBitRate = caps.SupportedBitRates.Count > 0 ? caps.SupportedBitRates[0] : null;
            _selectedDataBitRate = caps.SupportedDataBitRates.Count > 0 ? caps.SupportedDataBitRates[0] : null;
        }

        // IConnectionOptionsContext implementation
        public bool SupportsCan20 => Capabilities.SupportsCan20;
        public bool SupportsCanFd => Capabilities.SupportsCanFd;
        public bool SupportsListenOnly => Capabilities.SupportsListenOnly;
        public bool SupportsErrorCounters => Capabilities.SupportsErrorCounters;

        public bool UseCan20
        {
            get => _useCan20;
            set => SetProperty(ref _useCan20, value);
        }

        public bool UseCanFd
        {
            get => _useCanFd;
            set => SetProperty(ref _useCanFd, value);
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

        public bool ListenOnly
        {
            get => _listenOnly;
            set => SetProperty(ref _listenOnly, value);
        }

        public int ErrorCountersPeriodMs
        {
            get => _errorCountersPeriodMs;
            set => SetProperty(ref _errorCountersPeriodMs, Math.Max(100, value));
        }

        public async Task StartAsync(CancellationToken externalToken)
        {
            Stop();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = _cts.Token;
            IsListening = true;

            await Task.Run(async () =>
            {
                try
                {
                    await _connectionService.StartAsync(
                        EndpointId,
                        UseCan20,
                        SelectedBitRate ?? 500_000,
                        SelectedDataBitRate ?? 5_000_000,
                        Filters,
                        Capabilities.Features,
                        (ListenOnly && Capabilities.SupportsListenOnly),
                        ErrorCountersPeriodMs,
                        (f, d) =>
                        {
                            // Aggregate to main frames collection with source
                            Dispatcher.UIThread.Post(() => _onFrameRow(FrameRow.From(f, d, EndpointId, DisplayName)));
                        },
                        msg => Dispatcher.UIThread.Post(() => _onLog(msg)),
                        _ => { /* per-device counters not shown in main for now */ },
                        _ => { /* per-device bus usage not shown */ },
                        token);
                }
                catch (OperationCanceledException)
                {
                    // normal
                }
                finally
                {
                    IsListening = false;
                }
            }, token).ConfigureAwait(false);
        }

        public void Stop()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
                _cts.Dispose();
                _cts = null;
            }
            IsListening = false;
        }

        public int Transmit(ICanFrame frame)
        {
            try
            {
                return _connectionService.Transmit(frame);
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
