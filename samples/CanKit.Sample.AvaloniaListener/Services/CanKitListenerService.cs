using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Services
{
    public class CanKitListenerService : IListenerService
    {
        private ICanBus? _bus;
        private readonly object _txLock = new();
        private Action<string>? _onMessage;
        private Action<ICanFrame, FrameDirection>? _onFrame;
        private readonly List<IPeriodicTx> _periodics = new();
        public async Task StartAsync(string endpoint,
            bool can20,
            int bitRate,
            int dataBitRate,
            IReadOnlyList<FilterRuleModel> filters,
            CanFeature features,
            bool listenOnly,
            int countersPollMs,
            Action<ICanFrame, FrameDirection> onFrame,
            Action<string> onMessage,
            Action<CanErrorCounters>? onCountersUpdated,
            Action<float>? onBusUsageUpdated,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint is empty", nameof(endpoint));

            var bus = CanBus.Open(endpoint, cfg =>
            {
                if (!can20)
                {
                    cfg.SetProtocolMode(CanProtocolMode.CanFd)
                       .Fd(bitRate, dataBitRate)
                       .SoftwareFeaturesFallBack(CanFeature.Filters | CanFeature.CyclicTx);
                }
                else
                {
                    cfg.SetProtocolMode(CanProtocolMode.Can20)
                       .Baud(bitRate)
                       .SoftwareFeaturesFallBack(CanFeature.Filters | CanFeature.CyclicTx);
                }
                if (listenOnly)
                {
                    cfg.SetWorkMode(ChannelWorkMode.ListenOnly);
                }
                if (filters is { Count: > 0 })
                {
                    foreach (var f in filters)
                    {
                        switch (f.Kind)
                        {
                            case Models.FilterKind.Mask:
                                cfg.AccMask(f.AccCode, f.AccMask, f.IdType);
                                break;
                            case Models.FilterKind.Range:
                                cfg.RangeFilter(f.From, f.To, f.IdType);
                                break;
                        }
                    }
                }
                if ((features & CanFeature.ErrorFrame) != 0)
                {
                    cfg.EnableErrorInfo();
                }
            });
            _bus = bus;
            _onMessage = onMessage;
            _onFrame = onFrame;
            if ((features & CanFeature.ErrorFrame) != 0)
            {
                bus.ErrorFrameReceived += (_, err) =>
                {
                    try
                    {
                        if (err.ErrorCounters is { } c1)
                        {
                            onCountersUpdated?.Invoke(c1);
                        }
                        else if ((features & CanFeature.ErrorCounters) != 0)
                        {
                            var c2 = bus.ErrorCounters();
                            onCountersUpdated?.Invoke(c2);
                        }
                        //onMessage($"[error] {err.Type} @{err.SystemTimestamp:HH:mm:ss.fff}");
                    }
                    catch { /* ignore */ }
                };
            }

            Task? countersPoller = null;
            if (((features & CanFeature.ErrorCounters) != 0 ||(features & CanFeature.BusUsage) != 0)
                && countersPollMs > 0)
            {
                var token = cancellationToken;
                countersPoller = Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            if ((features & CanFeature.ErrorCounters) != 0)
                            {
                                try
                                {
                                    var c = bus.ErrorCounters();
                                    onCountersUpdated?.Invoke(c);
                                }
                                catch { /* ignore polling errors */ }
                            }
                            if ((features & CanFeature.BusUsage) != 0)
                            {
                                try
                                {
                                    var c = bus.BusUsage();
                                    onBusUsageUpdated?.Invoke(c);
                                }
                                catch { /* ignore polling errors */ }
                            }
                            await Task.Delay(countersPollMs, token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { /* normal */ }
                }, cancellationToken);
            }
            bus.BackgroundExceptionOccurred += (_, ex) =>
            {
                onMessage($"[exception] {ex.Message}");
            };

            if (can20)
            {
                onMessage($"[info] Listening on '{endpoint}' @ {bitRate} bps, mode=CAN 2.0...");
            }
            else
            {
                onMessage($"[info] Listening on '{endpoint}' @ {bitRate} bps:{dataBitRate} bps, mode=CAN FD...");
            }

            try
            {
#if NET8_0_OR_GREATER
                await foreach (var rec in bus.GetFramesAsync(cancellationToken))
                {
                    LogFrame(rec.CanFrame, FrameDirection.Rx, onFrame);
                }
#else
                while (!cancellationToken.IsCancellationRequested)
                {
                    var list = await bus.ReceiveAsync(64, timeOut: 100, cancellationToken);
                    foreach (var rec in list)
                        LogFrame(rec.CanFrame, FrameDirection.Rx, onFrame);
                }
#endif
            }
            finally
            {
                StopPeriodic();
                onMessage("[info] Listener stopped.");
                _bus = null;
                bus.Dispose();
            }
        }

        public int Transmit(ICanFrame frame)
        {
            var bus = _bus;
            if (bus == null)
                return 0;
            try
            {
                lock (_txLock)
                {
                    var onFrame = _onFrame;
                    if (onFrame != null)
                        LogFrame(frame, FrameDirection.Tx, onFrame);
                    return bus.Transmit(frame);
                }
            }
            catch
            {
                return 0;
            }
        }

        private void LogFrame(ICanFrame f, FrameDirection dir, Action<ICanFrame, FrameDirection> onFrame)
        {
            try
            {
                onFrame?.Invoke(f, dir);
            }
            catch
            {
                // ignore UI callback errors
            }
        }

        public void StartPeriodic(IEnumerable<(ICanFrame frame, TimeSpan period)> items)
        {
            var bus = _bus ?? throw new InvalidOperationException("Bus not opened.");
            StopPeriodic();
            foreach (var (frame, period) in items)
            {
                var opt = new CanKit.Core.Definitions.PeriodicTxOptions(period, repeat: -1, fireImmediately: true);
                var tx = bus.TransmitPeriodic(frame, opt);
                _periodics.Add(tx);
            }
            _onMessage?.Invoke($"[info] Periodic started: {_periodics.Count} item(s).");
        }

        public void StopPeriodic()
        {
            if (_periodics.Count == 0)
                return;
            foreach (var p in _periodics)
            {
                try { p.Stop(); } catch { /* ignore */ }
                (p as IDisposable)?.Dispose();
            }
            _periodics.Clear();
            _onMessage?.Invoke("[info] Periodic stopped.");
        }
    }
}

