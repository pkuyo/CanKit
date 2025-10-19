using System;
using CanKit.Core.Abstractions;

namespace CanKit.Core.Definitions;

/// <summary>
/// Generic no-op device used for backends without a real device layer.
/// TOptions keeps strong typing for RT options access.
/// </summary>
public sealed class NullDevice<TOptions> : ICanDevice<DeviceRTOptionsConfigurator<TOptions>>
    where TOptions : class, IDeviceOptions
{
    private readonly TOptions _options;

    private bool _isOpen;

    public NullDevice(IDeviceOptions options)
    {
        if (options is not TOptions typed)
            throw new ArgumentException($"Options type mismatch: expected {typeof(TOptions).FullName}, got {options?.GetType().FullName ?? "<null>"}.");
        Options = new DeviceRTOptionsConfigurator<TOptions>();
        Options.Init(typed);
        _options = typed;
    }

    public void OpenDevice()
    {
        _isOpen = true;
    }

    public void CloseDevice()
    {
        _isOpen = false;
    }

    public bool IsDeviceOpen => _isOpen;

    public DeviceRTOptionsConfigurator<TOptions> Options { get; }

    IDeviceRTOptionsConfigurator ICanDevice.Options => Options;

    public void Dispose()
    {
        _isOpen = false;
    }
}


public sealed class NullDeviceInitOptionsConfigurator
    : DeviceInitOptionsConfigurator<NullDeviceOptions, NullDeviceInitOptionsConfigurator>;

public sealed class NullDeviceRTOptionsConfigurator
    : DeviceRTOptionsConfigurator<NullDeviceOptions>;

public sealed class NullDeviceOptions(ICanModelProvider provider) : IDeviceOptions
{
    public uint TxTimeOut { get; set; } = 100U;
    public DeviceType DeviceType { get; } = provider.DeviceType;
    public CanFeature Features { get; set; } = provider.StaticFeatures;
}
