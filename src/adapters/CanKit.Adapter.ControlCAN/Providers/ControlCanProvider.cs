using System.Collections.Generic;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ControlCAN.Providers;

public sealed class ControlCanRProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes { get; } =
    [
        ControlCanDeviceType.VCI_USBCAN_2E_U,
        ControlCanDeviceType.VCI_USBCAN_E_U,
        ControlCanDeviceType.VCI_USBCAN_4E_U,
        ControlCanDeviceType.VCI_USBCAN_8E_U,
    ];
    public ICanModelProvider Create(DeviceType deviceType) =>
        new ControlCanEProvider(deviceType);
}

public sealed class ControlCanProviderGroup : ICanModelProviderGroup
{
    public IEnumerable<DeviceType> SupportedDeviceTypes { get; } =
    [
        ControlCanDeviceType.VCI_PCI9810I,
        ControlCanDeviceType.VCI_USBCAN1,
        ControlCanDeviceType.VCI_USBCAN2,
        ControlCanDeviceType.VCI_PCI9820,
        ControlCanDeviceType.VCI_PCI9840I,
        ControlCanDeviceType.VCI_PCI9820I,
    ];
    public ICanModelProvider Create(DeviceType deviceType) =>
        new ControlCanProvider(deviceType);
}
