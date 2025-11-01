using System;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ControlCAN.Definitions;

public enum ControlCanDeviceKind : uint
{
    VCI_PCI9810I = 2,
    VCI_USBCAN1 = 3,
    VCI_USBCAN2 = 4,
    VCI_PCI9820 = 5,
    VCI_PCI9840I = 14,
    VCI_PCI9820I = 16,
    VCI_USBCAN_E_U = 20,
    VCI_USBCAN_2E_U = 21,
    VCI_USBCAN_4E_U = 31,
    VCI_USBCAN_8E_U = 34,
}

public sealed record ControlCanDeviceType : DeviceType
{
    public static readonly ControlCanDeviceType VCI_PCI9810I = new(nameof(VCI_PCI9810I), (int)ControlCanDeviceKind.VCI_PCI9810I);
    public static readonly ControlCanDeviceType VCI_USBCAN1 = new(nameof(VCI_USBCAN1), (int)ControlCanDeviceKind.VCI_USBCAN1);
    public static readonly ControlCanDeviceType VCI_USBCAN2 = new(nameof(VCI_USBCAN2), (int)ControlCanDeviceKind.VCI_USBCAN2);
    public static readonly ControlCanDeviceType VCI_PCI9820 = new(nameof(VCI_PCI9820), (int)ControlCanDeviceKind.VCI_PCI9820);
    public static readonly ControlCanDeviceType VCI_PCI9840I = new(nameof(VCI_PCI9840I), (int)ControlCanDeviceKind.VCI_PCI9840I);
    public static readonly ControlCanDeviceType VCI_PCI9820I = new(nameof(VCI_PCI9820I), (int)ControlCanDeviceKind.VCI_PCI9820I);
    public static readonly ControlCanDeviceType VCI_USBCAN_E_U = new(nameof(VCI_USBCAN_E_U), (int)ControlCanDeviceKind.VCI_USBCAN_E_U);
    public static readonly ControlCanDeviceType VCI_USBCAN_2E_U = new(nameof(VCI_USBCAN_2E_U), (int)ControlCanDeviceKind.VCI_USBCAN_2E_U);
    public static readonly ControlCanDeviceType VCI_USBCAN_4E_U = new(nameof(VCI_USBCAN_4E_U), (int)ControlCanDeviceKind.VCI_USBCAN_4E_U);
    public static readonly ControlCanDeviceType VCI_USBCAN_8E_U = new(nameof(VCI_USBCAN_8E_U), (int)ControlCanDeviceKind.VCI_USBCAN_8E_U);
    public ControlCanDeviceType(string id, int code) : base($"ControlCAN.{id}")
    {
        Code = code;
    }

    public int Code { get; }
}
