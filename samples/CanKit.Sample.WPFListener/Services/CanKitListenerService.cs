using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Services
{
    public class CanKitListenerService : IListenerService
    {
        private ICanBus? _bus;
        private readonly object _txLock = new();
        private Action<string>? _onMessage;
        public async Task StartAsync(string endpoint,
            bool can20,
            int bitRate,
            int dataBitRate,
            IReadOnlyList<FilterRuleModel> filters,
            Action<string> onMessage,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("Endpoint is empty", nameof(endpoint));

            // Open the bus with requested mode and bit timing
            var bus = CanBus.Open(endpoint, cfg =>
            {
                if (!can20)
                {
                    cfg.SetProtocolMode(CanProtocolMode.CanFd)
                       .Fd(bitRate, dataBitRate)
                       .SoftwareFeaturesFallBack(CanFeature.Filters);
                }
                else
                {
                    cfg.SetProtocolMode(CanProtocolMode.Can20)
                       .Baud(bitRate)
                       .SoftwareFeaturesFallBack(CanFeature.Filters);
                }
                // Apply filters if any
                if (filters is { Count: > 0 })
                {
                    foreach (var f in filters)
                    {
                        switch (f.Kind)
                        {
                            case EndpointListenerWpf.Models.FilterKind.Mask:
                                cfg.AccMask(f.AccCode, f.AccMask, f.IdType);
                                break;
                            case EndpointListenerWpf.Models.FilterKind.Range:
                                cfg.RangeFilter(f.From, f.To, f.IdType);
                                break;
                        }
                    }
                }
                // Optional: enable error info if supported
                // cfg.EnableErrorInfo();
            });
            _bus = bus;
            _onMessage = onMessage;
            /*
            bus.ErrorFrameReceived += (_, err) =>
            {
                onMessage($"[error] {err.Type} @{err.SystemTimestamp:HH:mm:ss.fff} {err.ErrorCounters}");
            };
            */
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
                    LogFrame(rec.CanFrame, FrameDirection.Rx);
                }
#else
                while (!cancellationToken.IsCancellationRequested)
                {
                    var list = await bus.ReceiveAsync(64, timeOut: 100, cancellationToken);
                    foreach (var rec in list)
                        LogFrame(rec.CanFrame, FrameDirection.Rx);
                }
#endif
            }
            finally
            {
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
                    LogFrame(frame, FrameDirection.Tx);
                    return bus.Transmit(frame);
                }
            }
            catch
            {
                return 0;
            }
        }

        private void LogFrame(ICanFrame f, FrameDirection dir)
        {
            var kind = f.FrameKind == CanFrameType.CanFd ? "FD" : "2.0";
            var data = f.Data.Span;
            var hex = data.Length == 0 ? string.Empty : Convert.ToHexString(data).ToLowerInvariant();
            if (hex.Length > 0)
            {
                // insert spaces between bytes for readability
                var spaced = string.Join(" ", Enumerable.Range(0, hex.Length / 2)
                    .Select(i => hex.Substring(i * 2, 2)));
                _onMessage?.Invoke($"[{dir}] {DateTime.Now:HH:mm:ss.fff}  {kind} ID=0x{f.ID:X3} DLC={f.Dlc} DATA={spaced}");
            }
            else
            {
                _onMessage?.Invoke($"[{dir}] {DateTime.Now:HH:mm:ss.fff}  {kind} ID=0x{f.ID:X3} DLC={f.Dlc}");
            }
        }
    }
}
