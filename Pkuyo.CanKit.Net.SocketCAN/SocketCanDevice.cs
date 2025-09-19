using System;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.SocketCAN;

public sealed class SocketCanDevice : ICanDevice<SocketCanDeviceRTOptionsConfigurator>, ICanApplier
{
    public SocketCanDevice(IDeviceOptions options)
    {
        Options = new SocketCanDeviceRTOptionsConfigurator();
        Options.Init((SocketCanDeviceOptions)options);
        _initOptions = (SocketCanDeviceOptions)options;
    }

    public void OpenDevice()
    {
        _isOpen = true; // Nothing to open globally for socketcan; channels open their own sockets.
    }

    public void CloseDevice()
    {
        _isOpen = false;
    }

    public bool IsDeviceOpen => _isOpen;

    public SocketCanDeviceRTOptionsConfigurator Options { get; }

    IDeviceRTOptionsConfigurator ICanDevice.Options => Options;

    public void Dispose()
    {
        _isOpen = false;
    }
    

    public void Apply(ICanOptions options)
    {
        // TODO
    }

    public CanOptionType ApplierStatus => IsDeviceOpen ? CanOptionType.Runtime : CanOptionType.Init;

    private bool _isOpen;
    private readonly SocketCanDeviceOptions _initOptions;
}

