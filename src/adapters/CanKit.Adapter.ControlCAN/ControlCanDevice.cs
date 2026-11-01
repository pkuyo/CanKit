using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using System;
using System.Collections.Generic;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Native;
using CanKit.Adapter.ControlCAN.Diagnostics;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core.Exceptions;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN;

public sealed class ControlCanDevice : ICanDevice<ControlCanDeviceRTOptionsConfigurator>
{
    private bool _isDisposed;
    private readonly IDeviceOptions _options;
    // A failed native clear must survive disposal/recreation of the managed device.
    // Only a successful clear removes an entry; closing the device is not proof that
    // firmware discarded its table. Identity is scoped to this process and driver index.
    private static readonly HashSet<(uint Type, uint Index, uint Channel)> ChannelsWithRangeFilters = new();
    private static readonly HashSet<(uint Type, uint Index, uint Channel)> OpenChannels = new();
    private readonly uint _rawDeviceType;
    private readonly uint _deviceIndex;

    internal void AcquireChannel(uint channel)
    {
        lock (OpenChannels)
        {
            if (!OpenChannels.Add((_rawDeviceType, _deviceIndex, channel)))
                throw new CanBusCreationException($"ControlCAN channel {channel} is already open or still stopping.");
        }
    }

    internal void ReleaseChannel(uint channel)
    {
        lock (OpenChannels) OpenChannels.Remove((_rawDeviceType, _deviceIndex, channel));
    }

    internal bool HasRangeFilters(uint channel)
    {
        lock (ChannelsWithRangeFilters)
            return ChannelsWithRangeFilters.Contains((_rawDeviceType, _deviceIndex, channel));
    }

    internal void SetRangeFilterState(uint channel, bool configured)
    {
        lock (ChannelsWithRangeFilters)
        {
            var key = (_rawDeviceType, _deviceIndex, channel);
            if (configured) ChannelsWithRangeFilters.Add(key);
            else ChannelsWithRangeFilters.Remove(key);
        }
    }

    public ControlCanDevice(IDeviceOptions options)
    {
        ControlCanPlatformGuard.EnsureSupported();
        Options = new ControlCanDeviceRTOptionsConfigurator();
        Options.Init((ControlCanDeviceOptions)options);
        _options = options;
        _rawDeviceType = (uint)((ControlCanDeviceType)Options.DeviceType).Code;
        _deviceIndex = Options.DeviceIndex;

        try
        {
            var dt = (ControlCanDeviceType)Options.DeviceType;
            var ok = CcApi.VCI_OpenDevice((uint)dt.Code, Options.DeviceIndex, 0);
            if (ok == 0)
                throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"VCI_OpenDevice failed for {dt.Id} index {Options.DeviceIndex}");
        }
        catch (Exception ex) when (NativeLibraryLoad.IsFailure(ex))
        {
            throw ControlCanException.NativeLibraryNotFound("Open", ex);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {
            var dt = (ControlCanDeviceType)Options.DeviceType;
            _ = CcApi.VCI_CloseDevice((uint)dt.Code, Options.DeviceIndex);
        }
        finally
        {
            _isDisposed = true;
        }
    }

    public ControlCanDeviceRTOptionsConfigurator Options { get; }
    IDeviceRTOptionsConfigurator ICanDevice.Options => Options;

    public void ApplyConfig(ICanOptions options)
    {
        // no-op at device scope for ControlCAN backend
    }
}
