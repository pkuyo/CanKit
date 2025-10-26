using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.Services;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Views;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class MainViewModel : ObservableObject
    {

        public ObservableCollection<string> Logs { get; } = new();
        public FixedSizeObservableCollection<FrameRow> AllFrames { get; } = new(20000);
        public FixedSizeObservableCollection<FrameRow> Frames { get; } = new(10000);
        public ObservableCollection<DeviceSessionViewModel> ConnectedDevices { get; } = new();
        public ObservableCollection<SendListItem> SendItems { get; } = new();
        public SendListItem? SelectedSendItem { get; set; }
        public RelayCommand CopyFrameToClipboardCommand { get; }
        public RelayCommand SelectAllDevicesCommand { get; }
        public RelayCommand SelectNoneDevicesCommand { get; }
        public AsyncRelayCommand AddSendItemCommand { get; }
        public AsyncRelayCommand EditSendItemCommand { get; }
        public RelayCommand RemoveSendItemCommand { get; }
        public RelayCommand SendEnabledCommand { get; }

        public AsyncRelayCommand OpenDeviceManagerCommand { get; }
        public AsyncRelayCommand OpenSettingsCommand { get; }

        public MainViewModel()
        {

            CopyFrameToClipboardCommand = new RelayCommand(p =>
            {
                if (p is FrameRow row)
                {
                    CopyFrameToClipboard(row);
                }
            }, p => p is FrameRow);

            SelectAllDevicesCommand = new RelayCommand(_ =>
            {
                foreach (var d in ConnectedDevices)
                    d.IsSelectedForView = true;
                ApplyFrameFilter();
            });
            SelectNoneDevicesCommand = new RelayCommand(_ =>
            {
                foreach (var d in ConnectedDevices)
                    d.IsSelectedForView = false;
                ApplyFrameFilter();
            });

            AddSendItemCommand = new AsyncRelayCommand(async _ => await AddSendItemAsync());

            EditSendItemCommand = new AsyncRelayCommand(async p => await EditSendItemAsync(p as SendListItem),
                p => p is SendListItem || SelectedSendItem != null);
            RemoveSendItemCommand = new RelayCommand(p => RemoveSendItem(p as SendListItem ?? SelectedSendItem!),
                p => p is SendListItem || SelectedSendItem != null);
            SendEnabledCommand = new RelayCommand(_ => SendEnabled());

            OpenDeviceManagerCommand = new AsyncRelayCommand(async (p) =>
            {
                var dlg = new DeviceManagerWindow(this);
                var app = Application.Current;
                var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                await dlg.ShowDialog(owner!);
            });
            OpenSettingsCommand = new AsyncRelayCommand(async (p) =>
            {
                var dlg = new Views.SettingsWindow();
                var app = Application.Current;
                var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                await dlg.ShowDialog(owner!);
            });
            ConnectedDevices.CollectionChanged += ConnectedDevices_CollectionChanged;
            SendItems.CollectionChanged += SendItems_CollectionChanged;
        }

        public void AddFrame(FrameRow row)
        {
            AllFrames.Add(row);
            if (IsDeviceSelected(row.SourceId))
            {
                Frames.Add(row);
            }
        }

        private bool IsDeviceSelected(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
                return true; // legacy frames from single-listener
            foreach (var d in ConnectedDevices)
            {
                if (d.EndpointId == sourceId)
                    return d.IsSelectedForView;
            }
            return true;
        }

        public void ApplyFrameFilter()
        {
            Frames.Clear();
            foreach (var f in AllFrames)
            {
                if (IsDeviceSelected(f.SourceId))
                    Frames.Add(f);
            }
        }

        private void CopyFrameToClipboard(FrameRow row)
        {
            try
            {
                var text = $"{row.Time} {row.Dir} {row.Kind} {row.Id} {row.Dlc} {row.Data}".TrimEnd();
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

        private async Task AddSendItemAsync()
        {
            ICanFrame? frame = await ShowSendDialogAsync(null);
            if (frame != null)
            {
                var dev = ConnectedDevices.FirstOrDefault();
                SendItems.Add(new SendListItem(frame, dev));
            }
        }

        private async Task EditSendItemAsync(SendListItem? item)
        {
            item ??= SelectedSendItem;
            if (item == null) return;
            ICanFrame? frame = await ShowSendDialogAsync(item.Frame);
            if (frame != null)
            {
                item.Frame = frame;
            }
        }

        private void RemoveSendItem(SendListItem item)
        {
            SendItems.Remove(item);
        }

        private void SendEnabled()
        {
            foreach (var it in SendItems)
            {
                if (!it.IsEnabled) continue;
                if (it.SelectedDevice == null) continue;
                try
                {
                    var n = it.SelectedDevice.Transmit(it.Frame);
                    if (n <= 0)
                        Logs.Add($"[warn] Frame not sent by {it.SelectedDevice.DisplayName}.");
                }
                catch (Exception ex)
                {
                    Logs.Add($"[error] Send failed: {ex.Message}");
                }
            }
        }

        private async Task<ICanFrame?> ShowSendDialogAsync(ICanFrame? initial)
        {
            var tcs = new TaskCompletionSource<ICanFrame?>();
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    Window? owner = null;
                    var app = Application.Current;
                    if (app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
                        {
                            MainWindow: { } w
                        })
                        owner = w;

                    var dlg = new EditFrameDialog(initial) { AllowFd = true };
                    var result = await dlg.ShowDialog<ICanFrame?>(owner!);
                    tcs.SetResult(result);
                }
                catch
                {
                    tcs.SetResult(null);
                }
            });
            return await tcs.Task.ConfigureAwait(false);
        }

        private void ConnectedDevices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var old in e.OldItems)
                {
                    if (old is DeviceSessionViewModel d)
                    {
                        foreach (var it in SendItems.ToList())
                        {
                            if (it.SelectedDevice == d)
                                it.SelectedDevice = null;
                        }
                    }
                }
            }
        }

        private readonly Dictionary<SendListItem, CancellationTokenSource> _sendLoops = new();

        private void SendItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (var ni in e.NewItems)
                {
                    if (ni is SendListItem it)
                    {
                        it.PropertyChanged += SendItem_PropertyChanged;
                        EvaluateSendItemLoop(it);
                    }
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (var oi in e.OldItems)
                {
                    if (oi is SendListItem it)
                    {
                        it.PropertyChanged -= SendItem_PropertyChanged;
                        StopSendItemLoop(it);
                    }
                }
            }
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var kv in _sendLoops.ToList())
                    StopSendItemLoop(kv.Key);
            }
        }

        private void SendItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not SendListItem it) return;
            if (e.PropertyName == nameof(SendListItem.IsEnabled) ||
                e.PropertyName == nameof(SendListItem.DelayMs) ||
                e.PropertyName == nameof(SendListItem.SelectedDevice) ||
                e.PropertyName == nameof(SendListItem.Frame))
            {
                EvaluateSendItemLoop(it);
            }
        }

        private void EvaluateSendItemLoop(SendListItem it)
        {
            if (it.IsEnabled && it.DelayMs > 0 && it.SelectedDevice != null)
                StartSendItemLoop(it);
            else
                StopSendItemLoop(it);
        }

        private void StartSendItemLoop(SendListItem it)
        {
            if (_sendLoops.ContainsKey(it)) return;
            var cts = new CancellationTokenSource();
            _sendLoops[it] = cts;
            var token = cts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var dev = it.SelectedDevice;
                        if (dev != null)
                        {
                            dev.Transmit(it.Frame);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logs.Add($"[error] Auto send failed: {ex.Message}");
                    }
                    try
                    {
                        await Task.Delay(it.DelayMs, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { }
                }
            }, token);
        }

        private void StopSendItemLoop(SendListItem it)
        {
            if (_sendLoops.Remove(it, out var cts))
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
        }
    }
}


