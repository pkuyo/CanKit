using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.SocketCAN;

public sealed class SocketCanDeviceOptions(ICanModelProvider provider) : IDeviceOptions
{
    public ICanModelProvider Provider { get; } = provider;
    public DeviceType DeviceType => Provider.DeviceType;
    public uint TxTimeOut { get; set; } = 100U;
    public void Apply(ICanApplier applier, bool force = false) => applier.Apply(this);
}

public sealed class SocketCanChannelOptions(ICanModelProvider provider) : IChannelOptions
{
    public ICanModelProvider Provider { get; } = provider;

    public int ChannelIndex { get; set; }
    public BitTiming BitTiming { get; set; } = new(500_000, null, null);
    public bool InternalResistance { get; set; }
    public bool BusUsageEnabled { get; set; }
    public uint BusUsagePeriodTime { get; set; } = 1000U;
    public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
    public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.NoRetry;
    public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
    public CanFilter Filter { get; set; } = new ();

    // SocketCAN specific: interface name, e.g. "can0", "vcan0" etc.
    public string InterfaceName { get; set; } = "can0";
    public void Apply(ICanApplier applier, bool force = false) => applier.Apply(this);
}

public sealed class SocketCanDeviceInitOptionsConfigurator
    : DeviceInitOptionsConfigurator<SocketCanDeviceOptions, SocketCanDeviceInitOptionsConfigurator>
{
}

public sealed class SocketCanDeviceRTOptionsConfigurator
    : DeviceRTOptionsConfigurator<SocketCanDeviceOptions>
{ }

public sealed class SocketCanChannelInitConfigurator
    : ChannelInitOptionsConfigurator<SocketCanChannelOptions, SocketCanChannelInitConfigurator>
{
    public SocketCanChannelInitConfigurator UseInterface(string name)
    {
        Options.InterfaceName = name;
        return this;
    }
}

public sealed class SocketCanChannelRTConfigurator
    : ChannelRTOptionsConfigurator<SocketCanChannelOptions>
{
    public string InterfaceName => Options.InterfaceName;
}
