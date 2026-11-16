using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Candlelight.Native;

namespace CanKit.Adapter.Candlelight;

public sealed class CandlelightDevice : ICanDevice<CandlelightDeviceRtConfigurator>
{
    private CandlelightSession? _session;

    internal CandlelightDevice(CandlelightDeviceOptions options) : this(options, CandleApi.Instance) { }

    internal CandlelightDevice(CandlelightDeviceOptions options, ICandleApi api)
    {
        Options = new CandlelightDeviceRtConfigurator();
        Options.Init(options);
        _session = CandlelightSession.Acquire(options.DeviceIndex, api);
    }

    public CandlelightDeviceRtConfigurator Options { get; }
    IDeviceRTOptionsConfigurator ICanDevice.Options => Options;
    internal CandlelightSession RetainSession()
    {
        var session = Volatile.Read(ref _session) ?? throw new ObjectDisposedException(nameof(CandlelightDevice));
        session.Retain();
        return session;
    }

    public void ApplyConfig(ICanOptions options) => throw new NotSupportedException("Candlelight device options cannot be changed after opening.");
    public void Dispose() => Interlocked.Exchange(ref _session, null)?.Release();
}

internal sealed class CandlelightSession
{
    private static readonly object SessionsGate = new();
    private static readonly Dictionary<uint, CandlelightSession> Sessions = new();
    private readonly object _gate = new();
    private readonly Dictionary<byte, CandlelightBus> _channels = new();
    private readonly uint _index;
    private readonly IntPtr _handle;
    private readonly ICandleApi _api;
    private readonly Thread _reader;
    private int _references = 1;
    private bool _stopping;
    private Exception? _fault;

    private CandlelightSession(uint index, ICandleApi api)
    {
        if (ReferenceEquals(api, CandleApi.Instance) && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Candle.dll is supported only on Windows.");
        if (index > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(index));
        _index = index;
        _api = api;
        IntPtr list = IntPtr.Zero;
        IntPtr device = IntPtr.Zero;
        var opened = false;
        try
        {
            if (!api.Scan(out list))
                throw new InvalidOperationException("Candle device scan failed.");
            if (!api.ListLength(list, out var count) || index >= count)
                throw new InvalidOperationException($"Candle device index {index} is not available.");
            if (!api.GetDevice(list, (byte)index, out device))
                throw new InvalidOperationException($"Could not retrieve Candle device {index}.");
            if (!api.Open(device))
                throw Error(api, device, "open device");
            opened = true;
            _handle = device;
        }
        catch
        {
            if (opened) api.Close(device);
            if (device != IntPtr.Zero) api.FreeDevice(device);
            throw;
        }
        finally
        {
            if (list != IntPtr.Zero) api.FreeList(list);
        }
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = $"Candlelight {index}" };
        _reader.Start();
    }

    internal static CandlelightSession Acquire(uint index, ICandleApi api)
    {
        lock (SessionsGate)
        {
            if (Sessions.TryGetValue(index, out var session))
            {
                if (!ReferenceEquals(session._api, api)) throw new InvalidOperationException("The device is already open with a different native API.");
                session._references++;
                return session;
            }
            var created = new CandlelightSession(index, api);
            Sessions.Add(index, created);
            return created;
        }
    }

    internal void Retain()
    {
        lock (SessionsGate)
        {
            if (!Sessions.TryGetValue(_index, out var current) || !ReferenceEquals(current, this))
                throw new ObjectDisposedException(nameof(CandlelightDevice));
            _references++;
        }
    }

    internal void Release()
    {
        lock (SessionsGate)
        {
            if (--_references != 0) return;
            Sessions.Remove(_index);
            lock (_gate)
            {
                _stopping = true;
                Monitor.PulseAll(_gate);
            }
            _reader.Join();
            lock (_gate)
            {
                _api.Close(_handle);
                _api.FreeDevice(_handle);
            }
        }
    }

    internal IntPtr Handle => _handle;

    internal void Start(byte channel, CandlelightBus bus, CandlelightBusOptions options)
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            if (_channels.ContainsKey(channel))
                throw new InvalidOperationException($"Candle channel {channel} is already open.");
            if (!_api.ChannelCount(_handle, out var count))
                throw Error(_api, _handle, "query channel count");
            if (channel >= count)
                throw new ArgumentOutOfRangeException(nameof(channel));
            if (!_api.GetCapabilities(_handle, channel, out var caps))
                throw Error(_api, _handle, "query channel capabilities");
            var nominal = options.BitTiming.Classic?.Nominal ?? throw new ArgumentException("Classic CAN timing is required.");
            var clockMHz = options.BitTiming.Classic?.clockMHz;
            if (clockMHz.HasValue && (ulong)clockMHz.Value * 1_000_000 != caps.Clock)
                throw new ArgumentException("The configured CAN clock does not match the device clock.");
            if (nominal.Segments is { } configured &&
                (configured.Tseg1 < caps.Tseg1Min || configured.Tseg1 > caps.Tseg1Max ||
                 configured.Tseg2 < caps.Tseg2Min || configured.Tseg2 > caps.Tseg2Max ||
                 configured.Sjw == 0 || configured.Sjw > caps.SjwMax || configured.Sjw > configured.Tseg2 ||
                 configured.Brp < caps.BrpMin || configured.Brp > caps.BrpMax ||
                 caps.BrpIncrement == 0 || configured.Brp % caps.BrpIncrement != 0))
                throw new ArgumentException("The configured bit timing is outside the device capabilities.");
            var timing = nominal.Segments is { } segments
                ? new CandleNative.BitTiming { PropSeg = segments.Tseg1 / 2, PhaseSeg1 = segments.Tseg1 - segments.Tseg1 / 2, PhaseSeg2 = segments.Tseg2, Sjw = segments.Sjw, Brp = segments.Brp }
                : CandleTiming.Calculate(caps, nominal.Bitrate ?? throw new ArgumentException("A CAN bitrate is required."), nominal.SamplePointPermille ?? 875);
            var mode = CandleNative.Mode.Normal;
            if (options.WorkMode == ChannelWorkMode.ListenOnly)
                mode |= CandleNative.Mode.ListenOnly;
            if (options.TxRetryPolicy == TxRetryPolicy.NoRetry)
                mode |= CandleNative.Mode.OneShot;
            if (options.AllowErrorInfo)
                mode |= CandleNative.Mode.BusErrorReporting;
            if (((uint)mode & ~caps.Feature) != 0)
                throw new NotSupportedException("The Candle device does not support the requested channel mode.");
            if ((caps.Feature & (uint)CandleNative.Mode.HardwareTimestamp) != 0)
                mode |= CandleNative.Mode.HardwareTimestamp;
            if (!_api.SetTiming(_handle, channel, ref timing))
                throw Error(_api, _handle, "set bit timing");
            if (!_api.Start(_handle, channel, mode))
                throw Error(_api, _handle, "start channel");
            _channels.Add(channel, bus);
            Monitor.PulseAll(_gate);
            options.Features = CanFeature.CanClassic | CanFeature.Echo |
                ((caps.Feature & (uint)CandleNative.Mode.ListenOnly) != 0 ? CanFeature.ListenOnly : 0) |
                ((caps.Feature & (uint)CandleNative.Mode.OneShot) != 0 ? CanFeature.TxRetryPolicy : 0) |
                ((caps.Feature & (uint)CandleNative.Mode.BusErrorReporting) != 0 ? CanFeature.ErrorFrame : 0);
            options.Capabilities = new Capability(options.Features);
        }
    }

    internal void Stop(byte channel, CandlelightBus bus)
    {
        lock (_gate)
        {
            if (_channels.TryGetValue(channel, out var current) && ReferenceEquals(current, bus))
            {
                _channels.Remove(channel);
                if (!_api.Stop(_handle, channel))
                    throw Error(_api, _handle, "stop channel");
            }
        }
    }

    internal void Send(byte channel, ref CandleNative.Frame frame)
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            if (!_channels.ContainsKey(channel)) throw new ObjectDisposedException(nameof(CandlelightBus));
            if (!_api.Send(_handle, channel, ref frame))
                throw Error(_api, _handle, "send frame");
        }
    }

    private void ReadLoop()
    {
        try
        {
            while (true)
            {
                CandleNative.Frame frame;
                CandlelightBus? destination = null;
                lock (_gate)
                {
                    while (!_stopping && _channels.Count == 0) Monitor.Wait(_gate);
                    if (_stopping) return;
                    if (_api.Read(_handle, out frame, 10))
                        _channels.TryGetValue(frame.Channel, out destination);
                    else if (_api.LastError(_handle) != CandleNative.ReadTimeout)
                        throw Error(_api, _handle, "read frame");
                }
                destination?.Enqueue(frame);
                Thread.Yield();
            }
        }
        catch (Exception ex)
        {
            CandlelightBus[] channels;
            lock (_gate)
            {
                _fault = ex;
                channels = new CandlelightBus[_channels.Count];
                _channels.Values.CopyTo(channels, 0);
            }
            foreach (var channel in channels) channel.EnqueueFault(ex);
        }
    }

    private void ThrowIfUnavailable()
    {
        if (_stopping) throw new ObjectDisposedException(nameof(CandlelightSession));
        if (_fault != null) throw new InvalidOperationException("The Candle device reader has stopped.", _fault);
    }

    private static Exception Error(ICandleApi api, IntPtr device, string operation) =>
        new InvalidOperationException($"Candle failed to {operation} (error {api.LastError(device)}).");
}
