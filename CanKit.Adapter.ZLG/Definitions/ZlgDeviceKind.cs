using System;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG.Definitions
{
    public enum ZlgDeviceKind : UInt32
    {
        ZCAN_PCI9810 = 2,
        ZCAN_USBCAN1 = 3,
        ZCAN_USBCAN2 = 4,
        ZCAN_PCI9820 = 5,
        ZCAN_CANETUDP = 12,
        ZCAN_PCI9840 = 14,
        ZCAN_PCI9820I = 16,
        ZCAN_CANETTCP = 17,
        ZCAN_PCI5010U = 19,
        ZCAN_USBCAN_E_U = 20,
        ZCAN_USBCAN_2E_U = 21,
        ZCAN_PCI5020U = 22,
        ZCAN_PCIE9221 = 24,
        ZCAN_WIFICAN_TCP = 25,
        ZCAN_WIFICAN_UDP = 26,
        ZCAN_PCIe9120 = 27,
        ZCAN_PCIe9110 = 28,
        ZCAN_PCIe9140 = 29,
        ZCAN_USBCAN_4E_U = 31,
        ZCAN_CANDTU_200UR = 32,
        ZCAN_USBCAN_8E_U = 34,
        ZCAN_CANDTU_NET = 36,
        ZCAN_CANDTU_100UR = 37,
        ZCAN_PCIE_CANFD_200U = 39,
        ZCAN_PCIE_CANFD_400U = 40,
        ZCAN_USBCANFD_200U = 41,
        ZCAN_USBCANFD_100U = 42,
        ZCAN_USBCANFD_MINI = 43,
        ZCAN_CANSCOPE = 45,
        ZCAN_CLOUD = 46,
        ZCAN_CANDTU_NET_400 = 47,
        ZCAN_CANFDNET_TCP = 48,
        ZCAN_CANFDNET_200U_TCP = 48,
        ZCAN_CANFDNET_UDP = 49,
        ZCAN_CANFDNET_200U_UDP = 49,
        ZCAN_CANFDWIFI_TCP = 50,
        ZCAN_CANFDWIFI_100U_TCP = 50,
        ZCAN_CANFDWIFI_UDP = 51,
        ZCAN_CANFDWIFI_100U_UDP = 51,
        ZCAN_CANFDNET_400U_TCP = 52,
        ZCAN_CANFDNET_400U_UDP = 53,
        ZCAN_CANFDNET_100U_TCP = 55,
        ZCAN_CANFDNET_100U_UDP = 56,
        ZCAN_CANFDNET_800U_TCP = 57,
        ZCAN_CANFDNET_800U_UDP = 58,
        ZCAN_USBCANFD_800U = 59,
        ZCAN_PCIE_CANFD_100U_EX = 60,
        ZCAN_PCIE_CANFD_400U_EX = 61,
        ZCAN_PCIE_CANFD_200U_MINI = 62,
        ZCAN_PCIE_CANFD_200U_EX = 63,
        ZCAN_PCIE_CANFD_200U_M2 = 63,
        ZCAN_CANFDDTU_400_TCP = 64,
        ZCAN_CANFDDTU_400_UDP = 65,
        ZCAN_CANFDWIFI_200U_TCP = 66,
        ZCAN_CANFDWIFI_200U_UDP = 67,
        ZCAN_CANFDDTU_800ER_TCP = 68,
        ZCAN_CANFDDTU_800ER_UDP = 69,
        ZCAN_CANFDDTU_800EWGR_TCP = 70,
        ZCAN_CANFDDTU_800EWGR_UDP = 71,
        ZCAN_CANFDDTU_600EWGR_TCP = 72,
        ZCAN_CANFDDTU_600EWGR_UDP = 73,
        ZCAN_CANFDDTU_CASCADE_TCP = 74,
        ZCAN_CANFDDTU_CASCADE_UDP = 75,
        ZCAN_USBCANFD_400U = 76,
        ZCAN_CANFDDTU_200U = 77,
        ZCAN_ZPSCANFD_TCP = 78,
        ZCAN_ZPSCANFD_USB = 79,
        ZCAN_CANFDBRIDGE_PLUS = 80,
        ZCAN_CANFDDTU_300U = 81,
        ZCAN_PCIE_CANFD_800U = 82,
        ZCAN_PCIE_CANFD_1200U = 83,
        ZCAN_MINI_PCIE_CANFD = 84,
        ZCAN_USBCANFD_800H = 85,
    }

    public sealed record ZlgDeviceType : DeviceType
    {
        public int Code { get; }

        public ZlgDeviceType(string id, int code) : base($"ZLG.{id}")
        {
            Code = code;
        }

        public static readonly ZlgDeviceType ZCAN_PCI9810 = new ZlgDeviceType(nameof(ZCAN_PCI9810), (int)ZlgDeviceKind.ZCAN_PCI9810);
        public static readonly ZlgDeviceType ZCAN_USBCAN1 = new ZlgDeviceType(nameof(ZCAN_USBCAN1), (int)ZlgDeviceKind.ZCAN_USBCAN1);
        public static readonly ZlgDeviceType ZCAN_USBCAN2 = new ZlgDeviceType(nameof(ZCAN_USBCAN2), (int)ZlgDeviceKind.ZCAN_USBCAN2);
        public static readonly ZlgDeviceType ZCAN_PCI9820 = new ZlgDeviceType(nameof(ZCAN_PCI9820), (int)ZlgDeviceKind.ZCAN_PCI9820);
        public static readonly ZlgDeviceType ZCAN_CANETUDP = new ZlgDeviceType(nameof(ZCAN_CANETUDP), (int)ZlgDeviceKind.ZCAN_CANETUDP);
        public static readonly ZlgDeviceType ZCAN_PCI9840 = new ZlgDeviceType(nameof(ZCAN_PCI9840), (int)ZlgDeviceKind.ZCAN_PCI9840);
        public static readonly ZlgDeviceType ZCAN_PCI9820I = new ZlgDeviceType(nameof(ZCAN_PCI9820I), (int)ZlgDeviceKind.ZCAN_PCI9820I);
        public static readonly ZlgDeviceType ZCAN_CANETTCP = new ZlgDeviceType(nameof(ZCAN_CANETTCP), (int)ZlgDeviceKind.ZCAN_CANETTCP);
        public static readonly ZlgDeviceType ZCAN_PCI5010U = new ZlgDeviceType(nameof(ZCAN_PCI5010U), (int)ZlgDeviceKind.ZCAN_PCI5010U);
        public static readonly ZlgDeviceType ZCAN_USBCAN_E_U = new ZlgDeviceType(nameof(ZCAN_USBCAN_E_U), (int)ZlgDeviceKind.ZCAN_USBCAN_E_U);
        public static readonly ZlgDeviceType ZCAN_USBCAN_2E_U = new ZlgDeviceType(nameof(ZCAN_USBCAN_2E_U), (int)ZlgDeviceKind.ZCAN_USBCAN_2E_U);
        public static readonly ZlgDeviceType ZCAN_PCI5020U = new ZlgDeviceType(nameof(ZCAN_PCI5020U), (int)ZlgDeviceKind.ZCAN_PCI5020U);
        public static readonly ZlgDeviceType ZCAN_PCIE9221 = new ZlgDeviceType(nameof(ZCAN_PCIE9221), (int)ZlgDeviceKind.ZCAN_PCIE9221);
        public static readonly ZlgDeviceType ZCAN_WIFICAN_TCP = new ZlgDeviceType(nameof(ZCAN_WIFICAN_TCP), (int)ZlgDeviceKind.ZCAN_WIFICAN_TCP);
        public static readonly ZlgDeviceType ZCAN_WIFICAN_UDP = new ZlgDeviceType(nameof(ZCAN_WIFICAN_UDP), (int)ZlgDeviceKind.ZCAN_WIFICAN_UDP);
        public static readonly ZlgDeviceType ZCAN_PCIe9120 = new ZlgDeviceType(nameof(ZCAN_PCIe9120), (int)ZlgDeviceKind.ZCAN_PCIe9120);
        public static readonly ZlgDeviceType ZCAN_PCIe9110 = new ZlgDeviceType(nameof(ZCAN_PCIe9110), (int)ZlgDeviceKind.ZCAN_PCIe9110);
        public static readonly ZlgDeviceType ZCAN_PCIe9140 = new ZlgDeviceType(nameof(ZCAN_PCIe9140), (int)ZlgDeviceKind.ZCAN_PCIe9140);
        public static readonly ZlgDeviceType ZCAN_USBCAN_4E_U = new ZlgDeviceType(nameof(ZCAN_USBCAN_4E_U), (int)ZlgDeviceKind.ZCAN_USBCAN_4E_U);
        public static readonly ZlgDeviceType ZCAN_CANDTU_200UR = new ZlgDeviceType(nameof(ZCAN_CANDTU_200UR), (int)ZlgDeviceKind.ZCAN_CANDTU_200UR);
        public static readonly ZlgDeviceType ZCAN_USBCAN_8E_U = new ZlgDeviceType(nameof(ZCAN_USBCAN_8E_U), (int)ZlgDeviceKind.ZCAN_USBCAN_8E_U);
        public static readonly ZlgDeviceType ZCAN_CANDTU_NET = new ZlgDeviceType(nameof(ZCAN_CANDTU_NET), (int)ZlgDeviceKind.ZCAN_CANDTU_NET);
        public static readonly ZlgDeviceType ZCAN_CANDTU_100UR = new ZlgDeviceType(nameof(ZCAN_CANDTU_100UR), (int)ZlgDeviceKind.ZCAN_CANDTU_100UR);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_200U = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_200U), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_200U);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_400U = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_400U), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_400U);
        public static readonly ZlgDeviceType ZCAN_USBCANFD_200U = new ZlgDeviceType(nameof(ZCAN_USBCANFD_200U), (int)ZlgDeviceKind.ZCAN_USBCANFD_200U);
        public static readonly ZlgDeviceType ZCAN_USBCANFD_100U = new ZlgDeviceType(nameof(ZCAN_USBCANFD_100U), (int)ZlgDeviceKind.ZCAN_USBCANFD_100U);
        public static readonly ZlgDeviceType ZCAN_USBCANFD_MINI = new ZlgDeviceType(nameof(ZCAN_USBCANFD_MINI), (int)ZlgDeviceKind.ZCAN_USBCANFD_MINI);
        public static readonly ZlgDeviceType ZCAN_CANSCOPE = new ZlgDeviceType(nameof(ZCAN_CANSCOPE), (int)ZlgDeviceKind.ZCAN_CANSCOPE);
        public static readonly ZlgDeviceType ZCAN_CLOUD = new ZlgDeviceType(nameof(ZCAN_CLOUD), (int)ZlgDeviceKind.ZCAN_CLOUD);
        public static readonly ZlgDeviceType ZCAN_CANDTU_NET_400 = new ZlgDeviceType(nameof(ZCAN_CANDTU_NET_400), (int)ZlgDeviceKind.ZCAN_CANDTU_NET_400);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_TCP), (int)ZlgDeviceKind.ZCAN_CANFDNET_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_200U_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_200U_TCP), (int)ZlgDeviceKind.ZCAN_CANFDNET_200U_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_UDP), (int)ZlgDeviceKind.ZCAN_CANFDNET_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_200U_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_200U_UDP), (int)ZlgDeviceKind.ZCAN_CANFDNET_200U_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDWIFI_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDWIFI_TCP), (int)ZlgDeviceKind.ZCAN_CANFDWIFI_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDWIFI_100U_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDWIFI_100U_TCP), (int)ZlgDeviceKind.ZCAN_CANFDWIFI_100U_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDWIFI_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDWIFI_UDP), (int)ZlgDeviceKind.ZCAN_CANFDWIFI_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDWIFI_100U_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDWIFI_100U_UDP), (int)ZlgDeviceKind.ZCAN_CANFDWIFI_100U_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_400U_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_400U_TCP), (int)ZlgDeviceKind.ZCAN_CANFDNET_400U_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_400U_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_400U_UDP), (int)ZlgDeviceKind.ZCAN_CANFDNET_400U_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_100U_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_100U_TCP), (int)ZlgDeviceKind.ZCAN_CANFDNET_100U_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_100U_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_100U_UDP), (int)ZlgDeviceKind.ZCAN_CANFDNET_100U_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_800U_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_800U_TCP), (int)ZlgDeviceKind.ZCAN_CANFDNET_800U_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDNET_800U_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDNET_800U_UDP), (int)ZlgDeviceKind.ZCAN_CANFDNET_800U_UDP);
        public static readonly ZlgDeviceType ZCAN_USBCANFD_800U = new ZlgDeviceType(nameof(ZCAN_USBCANFD_800U), (int)ZlgDeviceKind.ZCAN_USBCANFD_800U);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_100U_EX = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_100U_EX), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_100U_EX);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_400U_EX = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_400U_EX), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_400U_EX);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_200U_MINI = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_200U_MINI), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_200U_MINI);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_200U_EX = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_200U_EX), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_200U_EX);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_200U_M2 = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_200U_M2), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_200U_M2);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_400_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_400_TCP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_400_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_400_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_400_UDP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_400_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDWIFI_200U_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDWIFI_200U_TCP), (int)ZlgDeviceKind.ZCAN_CANFDWIFI_200U_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDWIFI_200U_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDWIFI_200U_UDP), (int)ZlgDeviceKind.ZCAN_CANFDWIFI_200U_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_800ER_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_800ER_TCP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_800ER_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_800ER_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_800ER_UDP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_800ER_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_800EWGR_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_800EWGR_TCP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_800EWGR_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_800EWGR_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_800EWGR_UDP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_800EWGR_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_600EWGR_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_600EWGR_TCP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_600EWGR_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_600EWGR_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_600EWGR_UDP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_600EWGR_UDP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_CASCADE_TCP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_CASCADE_TCP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_CASCADE_TCP);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_CASCADE_UDP = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_CASCADE_UDP), (int)ZlgDeviceKind.ZCAN_CANFDDTU_CASCADE_UDP);
        public static readonly ZlgDeviceType ZCAN_USBCANFD_400U = new ZlgDeviceType(nameof(ZCAN_USBCANFD_400U), (int)ZlgDeviceKind.ZCAN_USBCANFD_400U);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_200U = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_200U), (int)ZlgDeviceKind.ZCAN_CANFDDTU_200U);
        public static readonly ZlgDeviceType ZCAN_ZPSCANFD_TCP = new ZlgDeviceType(nameof(ZCAN_ZPSCANFD_TCP), (int)ZlgDeviceKind.ZCAN_ZPSCANFD_TCP);
        public static readonly ZlgDeviceType ZCAN_ZPSCANFD_USB = new ZlgDeviceType(nameof(ZCAN_ZPSCANFD_USB), (int)ZlgDeviceKind.ZCAN_ZPSCANFD_USB);
        public static readonly ZlgDeviceType ZCAN_CANFDBRIDGE_PLUS = new ZlgDeviceType(nameof(ZCAN_CANFDBRIDGE_PLUS), (int)ZlgDeviceKind.ZCAN_CANFDBRIDGE_PLUS);
        public static readonly ZlgDeviceType ZCAN_CANFDDTU_300U = new ZlgDeviceType(nameof(ZCAN_CANFDDTU_300U), (int)ZlgDeviceKind.ZCAN_CANFDDTU_300U);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_800U = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_800U), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_800U);
        public static readonly ZlgDeviceType ZCAN_PCIE_CANFD_1200U = new ZlgDeviceType(nameof(ZCAN_PCIE_CANFD_1200U), (int)ZlgDeviceKind.ZCAN_PCIE_CANFD_1200U);
        public static readonly ZlgDeviceType ZCAN_MINI_PCIE_CANFD = new ZlgDeviceType(nameof(ZCAN_MINI_PCIE_CANFD), (int)ZlgDeviceKind.ZCAN_MINI_PCIE_CANFD);
        public static readonly ZlgDeviceType ZCAN_USBCANFD_800H = new ZlgDeviceType(nameof(ZCAN_USBCANFD_800H), (int)ZlgDeviceKind.ZCAN_USBCANFD_800H);
    }
}
