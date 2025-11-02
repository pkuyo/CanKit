using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.SPI.Providers;

namespace CanKit.Abstractions.SPI.Registry.Core.Endpoints;

/// <summary>
/// Represents a prepared context for opening or sniffing a bus.
/// ZH: 表示用于打开或嗅探总线的已准备上下文（仅构造配置，不创建设备/通道）。
/// </summary>
public sealed class PreparedBusContext
{
    public PreparedBusContext(
        ICanModelProvider provider,
        IDeviceOptions deviceOptions,
        IDeviceInitOptionsConfigurator deviceConfigurator,
        IBusOptions busOptions,
        IBusInitOptionsConfigurator busConfigurator)
    {
        Provider = provider;
        DeviceOptions = deviceOptions;
        DeviceConfigurator = deviceConfigurator;
        BusOptions = busOptions;
        BusConfigurator = busConfigurator;
    }

    public ICanModelProvider Provider { get; }
    public IDeviceOptions DeviceOptions { get; }
    public IDeviceInitOptionsConfigurator DeviceConfigurator { get; }
    public IBusOptions BusOptions { get; }
    public IBusInitOptionsConfigurator BusConfigurator { get; }

    public void Deconstruct(
        out ICanModelProvider provider,
        out IDeviceOptions deviceOptions,
        out IDeviceInitOptionsConfigurator deviceConfigurator,
        out IBusOptions busOptions,
        out IBusInitOptionsConfigurator busConfigurator)
    {
        provider = Provider;
        deviceOptions = DeviceOptions;
        deviceConfigurator = DeviceConfigurator;
        busOptions = BusOptions;
        busConfigurator = BusConfigurator;
    }
}

