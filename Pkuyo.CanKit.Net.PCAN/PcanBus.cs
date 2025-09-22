using System.Reflection;
using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;

namespace Pkuyo.CanKit.Net.PCAN;

public sealed class PcanBus : ICanBus<PcanBusRtConfigurator>, ICanApplier, IBusOwnership
{
    internal PcanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new PcanBusRtConfigurator();
        Options.Init((PcanBusOptions)options);
        _options = (PcanBusOptions)options;
        _transceiver = transceiver;

        _handle = ParseHandle(_options.Channel);

        if (Options.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, Options.Features);

        var baud = MapClassicBaud(Options.BitTiming);
        var st = PCANBasic.Initialize(_handle, baud);
        if (st != TPCANStatus.PCAN_ERROR_OK)
            throw new CanBusCreationException($"PCAN Initialize failed: {st}");
    }


    public void Reset()
    {
        ThrowIfDisposed();
        _ = PCANBasic.Reset(_handle);
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        // Reset clears the receive/transmit queues
        _ = PCANBasic.Reset(_handle);

    }

    public uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Transmit(this, frames, timeOut);
    }

    public float BusUsage()
    {
        throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    }

    public CanErrorCounters ErrorCounters()
    {
        throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Features);
    }

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        return _transceiver.Receive(this, count, timeOut);
    }

    public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
    {
        errorInfo = null;
        return false;
    }

    public PcanBusRtConfigurator Options { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;


    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {

        }
        finally
        {
            _isDisposed = true;
            _owner?.Dispose();
        }
    }

    public void Apply(ICanOptions options)
    {
        // For now no-op. Future integration can map Options.Filter, BitTiming, etc.
    }

    public CanOptionType ApplierStatus => CanOptionType.Runtime;

    public event EventHandler<CanReceiveData>? FrameReceived
    {
        add { lock (_evtGate) { _frameReceived += value; } }
        remove { lock (_evtGate) { _frameReceived -= value; } }
    }

    public event EventHandler<ICanErrorInfo>? ErrorOccurred
    {
        add { lock (_evtGate) { _errorOccurred += value; } }
        remove { lock (_evtGate) { _errorOccurred -= value; } }
    }

    internal void OnFrameReceived(CanReceiveData data) => _frameReceived?.Invoke(this, data);
    internal void OnErrorOccurred(ICanErrorInfo info) => _errorOccurred?.Invoke(this, info);

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new ObjectDisposedException(GetType().FullName);
    }

    private readonly PcanBusOptions _options;
    private readonly ITransceiver _transceiver;
    private bool _isDisposed;
    private readonly object _evtGate = new();
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccurred;

    private IDisposable? _owner;
    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    internal ushort Handle => _handle;

    private ushort _handle;

    private static TPCANBaudrate MapClassicBaud(BitTiming timing)
    {
        var b = timing.BaudRate ?? 500_000u;
        return b switch
        {
            1_000_000 => TPCANBaudrate.PCAN_BAUD_1M,
            800_000 => TPCANBaudrate.PCAN_BAUD_800K,
            500_000 => TPCANBaudrate.PCAN_BAUD_500K,
            250_000 => TPCANBaudrate.PCAN_BAUD_250K,
            125_000 => TPCANBaudrate.PCAN_BAUD_125K,
            100_000 => TPCANBaudrate.PCAN_BAUD_100K,
            50_000 => TPCANBaudrate.PCAN_BAUD_50K,
            20_000 => TPCANBaudrate.PCAN_BAUD_20K,
            10_000 => TPCANBaudrate.PCAN_BAUD_10K,
            5_000 => TPCANBaudrate.PCAN_BAUD_5K,
            _ => throw new CanChannelConfigurationException($"Unsupported PCAN classic bitrate: {b}")
        };
    }

    private static ushort ParseHandle(string channel)
    {
        // Accept known constant names defined in BackwardCompatibility.PCANBasic
        string[] candidates = channel.StartsWith("PCAN_", StringComparison.OrdinalIgnoreCase)
            ? new[] { channel }
            : new[] { "PCAN_" + channel, channel };

        var fields = typeof(PCANBasic).GetFields(BindingFlags.Public | BindingFlags.Static);
        foreach (var name in candidates)
        {
            var f = fields.FirstOrDefault(fi => string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase));
            if (f != null)
            {
                var val = f.GetValue(null);
                if (val is ushort u) return u;
            }
        }

        if (int.TryParse(channel, out var idx))
        {
            var name = $"PCAN_USBBUS{Math.Max(1, idx + 1)}";
            var f = fields.FirstOrDefault(fi => string.Equals(fi.Name, name, StringComparison.OrdinalIgnoreCase));
            if (f != null && f.GetValue(null) is ushort u2) return u2;
        }

        throw new CanBusCreationException($"Unknown PCAN channel '{channel}'.");
    }
}
