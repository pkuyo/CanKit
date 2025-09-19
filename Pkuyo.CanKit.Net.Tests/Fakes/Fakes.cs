using System;
using System.Collections.Generic;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;

namespace Pkuyo.CanKit.Net.Tests.Fakes;

// Test device type registration
public static class TestDeviceTypes
{
    public static readonly DeviceType Test = DeviceType.Register("test-device", 10001);
}

// Options implementations
public sealed class TestDeviceOptions : IDeviceOptions
{
    public required ICanModelProvider Provider { get; init; }
    public DeviceType DeviceType => Provider.DeviceType;
    public uint TxTimeOut { get; set; } = 50;

    public void Apply(ICanApplier applier, bool force = false)
    {
        // Minimal no-op for tests
    }
}

public sealed class TestChannelOptions : IChannelOptions
{
    public required ICanModelProvider Provider { get; init; }
    public int ChannelIndex { get; set; }
    public BitTiming BitTiming { get; set; } = new BitTiming(500_000, null, null);
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.NoRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; }

    public void Apply(ICanApplier applier, bool force = false) { }
}

// Provider with configurable features
public sealed class TestModelProvider : ICanModelProvider
{
    public static readonly CanFeature FeaturesForTests =
        CanFeature.CanClassic | CanFeature.Filters | CanFeature.BusUsage | CanFeature.ErrorCounters;

    public TestModelProvider()
    {
        
    }

    public TestModelProvider(CanFeature? featuresOverride = null)
    {
        _featuresOverride = featuresOverride;
    }

    public DeviceType DeviceType => TestDeviceTypes.Test;
    public CanFeature Features => _featuresOverride ?? FeaturesForTests;
    public ICanFactory Factory { get; } = new TestFactory();

    private CanFeature? _featuresOverride;

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var opt = new TestDeviceOptions { Provider = this };
        var cfg = new TestDeviceInitCfg().Init(opt);
        return (opt, cfg);
    }

    public (IChannelOptions, IChannelInitOptionsConfigurator) GetChannelOptions(int channelIndex)
    {
        var opt = new TestChannelOptions { Provider = this, ChannelIndex = channelIndex };
        var cfg = new TestChannelInitCfg().Init(opt);
        return (opt, cfg);
    }
}

// Self-typed configurators to satisfy generic-of-self pattern
public sealed class TestDeviceInitCfg : DeviceInitOptionsConfigurator<TestDeviceOptions, TestDeviceInitCfg> { }
public sealed class TestChannelInitCfg : ChannelInitOptionsConfigurator<TestChannelOptions, TestChannelInitCfg> { }

// Factory
public sealed class TestFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options)
    {
        if (options is not TestDeviceOptions devOpt)
            throw new CanOptionTypeMismatchException(CanKitErrorCode.DeviceOptionTypeMismatch,
                typeof(TestDeviceOptions), options?.GetType() ?? typeof(IDeviceOptions), "device");
        return new TestDevice(devOpt);
    }

    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver)
    {
        if (options is not TestChannelOptions chOpt)
            throw new CanOptionTypeMismatchException(CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(TestChannelOptions), options?.GetType() ?? typeof(IChannelOptions), "channel");

        return new TestChannel((TestDevice)device, chOpt, transceiver);
    }

    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions)
    {
        return new FakeTransceiver();
    }

    public bool Support(DeviceType deviceType) => deviceType.Equals(TestDeviceTypes.Test);
}

// Device
public sealed class TestDevice : ICanDevice<IDeviceRTOptionsConfigurator>
{
    private readonly TestDeviceOptions _options;
    private readonly IDeviceRTOptionsConfigurator _rtCfg;

    public TestDevice(TestDeviceOptions options)
    {
        _options = options;
        _rtCfg = new DeviceRTOptionsConfigurator<TestDeviceOptions>().Init(_options);
    }

    public void OpenDevice() => IsDeviceOpen = true;
    public void CloseDevice() => IsDeviceOpen = false;
    public bool IsDeviceOpen { get; private set; }
    public IDeviceRTOptionsConfigurator Options => _rtCfg;
    public void Dispose() => CloseDevice();
}

// Channel
public sealed class TestChannel : ICanChannel<IChannelRTOptionsConfigurator>
{
    private readonly TestDevice _device;
    private readonly TestChannelOptions _options;
    private readonly FakeTransceiver _transceiver;
    private readonly IChannelRTOptionsConfigurator _rtCfg;
    private bool _opened;

    public TestChannel(TestDevice device, TestChannelOptions options, ITransceiver transceiver)
    {
        _device = device;
        _options = options;
        _transceiver = (FakeTransceiver)transceiver;
        _rtCfg = new ChannelRTOptionsConfigurator<TestChannelOptions>().Init(_options);
    }

    public void Open() { if (!_device.IsDeviceOpen) throw new CanDeviceNotOpenException(); _opened = true; }
    public void Close() { _opened = false; }
    public void Reset() { }
    public void ClearBuffer() { }
    public uint Transmit(params CanTransmitData[] frames)
    {
        if (!_opened) throw new CanChannelNotOpenException();
        return _transceiver.Transmit(this, frames);
    }
    public float BusUsage() => 12.34f;
    public CanErrorCounters ErrorCounters() => new() { ReceiveErrorCounter = 0, TransmitErrorCounter = 0 };
    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1)
    {
        if (!_opened) throw new CanChannelNotOpenException();
        var items = _transceiver.Receive(this, count, timeOut);
        foreach (var it in items) FrameReceived?.Invoke(this, it);
        return items;
    }
    public bool ReadChannelErrorInfo(out ICanErrorInfo errorInfo) { errorInfo = null; return false; }
    public uint GetReceiveCount() => 0;
    public IChannelRTOptionsConfigurator Options => _rtCfg;
    public event EventHandler<CanReceiveData> FrameReceived;
    public event EventHandler<ICanErrorInfo> ErrorOccurred;
    public void Dispose() => Close();
}

// Transceiver
public sealed class FakeTransceiver : ITransceiver
{
    public List<CanTransmitData> Sent { get; } = new();
    public List<CanReceiveData> ToReceive { get; } = new();

    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, params CanTransmitData[] frames)
    {
        Sent.AddRange(frames);
        return (uint)frames.Length;
    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int timeOut = -1)
    {
        if (ToReceive.Count == 0) return Array.Empty<CanReceiveData>();
        var take = (int)Math.Min(count, (uint)ToReceive.Count);
        var items = ToReceive.Take(take).ToArray();
        ToReceive.RemoveRange(0, take);
        return items;
    }
}
