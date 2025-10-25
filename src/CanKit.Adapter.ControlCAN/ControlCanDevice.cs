using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Native;
using CanKit.Adapter.ControlCAN.Options;
using CanKit.Core.Abstractions;
using CanKit.Core.Exceptions;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN;

public sealed class ControlCanDevice : ICanDevice<ControlCanDeviceRTOptionsConfigurator>
{
    private bool _isDisposed;
    private readonly IDeviceOptions _options;

    public ControlCanDevice(IDeviceOptions options)
    {
        Options = new ControlCanDeviceRTOptionsConfigurator();
        Options.Init((ControlCanDeviceOptions)options);
        _options = options;

        var dt = (ControlCanDeviceType)Options.DeviceType;
        var ok = CcApi.VCI_OpenDevice((uint)dt.Code, Options.DeviceIndex, 0);
        if (ok == 0)
            throw new CanFactoryException(CanKitErrorCode.DeviceCreationFailed, $"VCI_OpenDevice failed for {dt.Id} index {Options.DeviceIndex}");
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
