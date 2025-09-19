using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Tests.Fakes;

// Additional device types for negative-path tests
public static class BrokenDeviceTypes
{
    public static readonly DeviceType NullDeviceFactory = DeviceType.Register("tests-null-device-factory", 20001);
    public static readonly DeviceType NullTransceiverFactory = DeviceType.Register("tests-null-transceiver-factory", 20002);
    public static readonly DeviceType MismatchChannelFactory = DeviceType.Register("tests-mismatch-channel-factory", 20003);
    public static readonly DeviceType ExposedTransceiverFactory = DeviceType.Register("tests-exposed-transceiver-factory", 20004);
}

// Factories with special behaviors
public sealed class NullDeviceFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options) => null; // trigger DeviceCreationFailed
    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver) => new TestChannel((TestDevice)device, (TestChannelOptions)options, transceiver);
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions) => new FakeTransceiver();
    public bool Support(DeviceType deviceType) => deviceType.Equals(BrokenDeviceTypes.NullDeviceFactory);
}

public sealed class NullTransceiverFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options) => new TestDevice((TestDeviceOptions)options);
    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver) => new TestChannel((TestDevice)device, (TestChannelOptions)options, transceiver);
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions) => null; // trigger TransceiverMismatch
    public bool Support(DeviceType deviceType) => deviceType.Equals(BrokenDeviceTypes.NullTransceiverFactory);
}

public sealed class MismatchChannelFactory : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options) => new TestDevice((TestDeviceOptions)options);
    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver) => new TestChannel((TestDevice)device, (TestChannelOptions)options, transceiver); // will mismatch if TChannel != TestChannel
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions) => new FakeTransceiver();
    public bool Support(DeviceType deviceType) => deviceType.Equals(BrokenDeviceTypes.MismatchChannelFactory);
}

// Factory exposing a shared FakeTransceiver for event tests
public sealed class ExposedTransceiverFactory : ICanFactory
{
    public FakeTransceiver Transceiver { get; } = new();
    public ICanDevice CreateDevice(IDeviceOptions options) => new TestDevice((TestDeviceOptions)options);
    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver) => new TestChannel((TestDevice)device, (TestChannelOptions)options, transceiver);
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions) => Transceiver;
    public bool Support(DeviceType deviceType) => deviceType.Equals(BrokenDeviceTypes.ExposedTransceiverFactory);
}

// Providers that surface the above factories
public sealed class NullDeviceProvider : ICanModelProvider
{
    public DeviceType DeviceType => BrokenDeviceTypes.NullDeviceFactory;
    public CanFeature Features => TestModelProvider.FeaturesForTests;
    public ICanFactory Factory { get; } = new NullDeviceFactory();
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

public sealed class NullTransceiverProvider : ICanModelProvider
{
    public DeviceType DeviceType => BrokenDeviceTypes.NullTransceiverFactory;
    public CanFeature Features => TestModelProvider.FeaturesForTests;
    public ICanFactory Factory { get; } = new NullTransceiverFactory();
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

public sealed class MismatchChannelProvider : ICanModelProvider
{
    public DeviceType DeviceType => BrokenDeviceTypes.MismatchChannelFactory;
    public CanFeature Features => TestModelProvider.FeaturesForTests;
    public ICanFactory Factory { get; } = new MismatchChannelFactory();
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

public sealed class ExposedTransceiverProvider : ICanModelProvider
{
    public DeviceType DeviceType => BrokenDeviceTypes.ExposedTransceiverFactory;
    public CanFeature Features => TestModelProvider.FeaturesForTests;
    public ExposedTransceiverFactory ExposedFactory { get; } = new();
    public ICanFactory Factory => ExposedFactory;
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

// Extra types only used for generic type-parameter mismatch tests
public sealed class OtherDevice : ICanDevice
{
    public void OpenDevice() => throw new NotImplementedException();
    public void CloseDevice() => throw new NotImplementedException();
    public bool IsDeviceOpen => throw new NotImplementedException();
    public IDeviceRTOptionsConfigurator Options => throw new NotImplementedException();
    public void Dispose() => throw new NotImplementedException();
}

public sealed class OtherChannel : ICanChannel
{
    public void Open() => throw new NotImplementedException();
    public void Close() => throw new NotImplementedException();
    public void Reset() => throw new NotImplementedException();
    public void ClearBuffer() => throw new NotImplementedException();
    public uint Transmit(params CanTransmitData[] frames) => throw new NotImplementedException();
    public float BusUsage() => throw new NotImplementedException();
    public CanErrorCounters ErrorCounters() => throw new NotImplementedException();
    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1) => throw new NotImplementedException();
    public bool ReadChannelErrorInfo(out ICanErrorInfo errorInfo) { errorInfo = null; throw new NotImplementedException(); }
    public uint GetReceiveCount() => throw new NotImplementedException();
    public IChannelRTOptionsConfigurator Options => throw new NotImplementedException();
    public event EventHandler<CanReceiveData> FrameReceived;
    public event EventHandler<ICanErrorInfo> ErrorOccurred;
    public void Dispose() => throw new NotImplementedException();
}

// Dummy types for generic device option/configurator mismatch tests
public sealed class DummyDeviceOptions : IDeviceOptions
{
    public required ICanModelProvider Provider { get; init; }
    public DeviceType DeviceType => Provider.DeviceType;
    public uint TxTimeOut { get; set; }
    public void Apply(ICanApplier applier, bool force = false) { }
}

public sealed class DummyDeviceInitCfg : IDeviceInitOptionsConfigurator
{
    public required ICanModelProvider Provider { get; init; }
    public DeviceType DeviceType => Provider.DeviceType;
    public uint TxTimeOutTime => 0;
    public IDeviceInitOptionsConfigurator TxTimeOut(uint ms) => this;
}

// A factory discoverable via attribute to test registry.Factory
[CanFactory("tests-factory-attribute")]
public sealed class AttributedFactoryFake : ICanFactory
{
    public ICanDevice CreateDevice(IDeviceOptions options) => null;
    public ICanChannel CreateChannel(ICanDevice device, IChannelOptions options, ITransceiver transceiver) => null;
    public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IChannelInitOptionsConfigurator channelOptions) => null;
    public bool Support(DeviceType deviceType) => false;
}

