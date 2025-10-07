// @formatter:off
#nullable disable
#pragma warning disable IDE0055
using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ZLG.Definitions;

namespace CanKit.Adapter.ZLG.Native
{

    public class ZLGCAN
    {

        public const int TX_ECHO_FLAG = 0x20;

        public const int CANFD_BRS = 0x01;
        public const int CANFD_ESI = 0x02;
        #region 设备类型
        public static UInt32 ZCAN_PCI9810 = 2;
        public static UInt32 ZCAN_USBCAN1 = 3;
        public static UInt32 ZCAN_USBCAN2 = 4;
        public static UInt32 ZCAN_PCI9820 = 5;
        public static UInt32 ZCAN_CANETUDP = 12;
        public static UInt32 ZCAN_PCI9840 = 14;
        public static UInt32 ZCAN_PCI9820I = 16;
        public static UInt32 ZCAN_CANETTCP = 17;
        public static UInt32 ZCAN_PCI5010U = 19;
        public static UInt32 ZCAN_USBCAN_E_U = 20;
        public static UInt32 ZCAN_USBCAN_2E_U = 21;
        public static UInt32 ZCAN_PCI5020U = 22;
        public static UInt32 ZCAN_PCIE9221 = 24;
        public static UInt32 ZCAN_WIFICAN_TCP = 25;
        public static UInt32 ZCAN_WIFICAN_UDP = 26;
        public static UInt32 ZCAN_PCIe9120 = 27;
        public static UInt32 ZCAN_PCIe9110 = 28;
        public static UInt32 ZCAN_PCIe9140 = 29;
        public static UInt32 ZCAN_USBCAN_4E_U = 31;
        public static UInt32 ZCAN_CANDTU_200UR = 32;
        public static UInt32 ZCAN_USBCAN_8E_U = 34;
        public static UInt32 ZCAN_CANDTU_NET = 36;
        public static UInt32 ZCAN_CANDTU_100UR = 37;
        public static UInt32 ZCAN_PCIE_CANFD_200U = 39;
        public static UInt32 ZCAN_PCIE_CANFD_400U = 40;
        public static UInt32 ZCAN_USBCANFD_200U = 41;
        public static UInt32 ZCAN_USBCANFD_100U = 42;
        public static UInt32 ZCAN_USBCANFD_MINI = 43;
        public static UInt32 ZCAN_CANSCOPE = 45;
        public static UInt32 ZCAN_CLOUD = 46;
        public static UInt32 ZCAN_CANDTU_NET_400 = 47;
        public static UInt32 ZCAN_CANFDNET_TCP = 48;
        public static UInt32 ZCAN_CANFDNET_200U_TCP = 48;
        public static UInt32 ZCAN_CANFDNET_UDP = 49;
        public static UInt32 ZCAN_CANFDNET_200U_UDP = 49;
        public static UInt32 ZCAN_CANFDWIFI_TCP = 50;
        public static UInt32 ZCAN_CANFDWIFI_100U_TCP = 50;
        public static UInt32 ZCAN_CANFDWIFI_UDP = 51;
        public static UInt32 ZCAN_CANFDWIFI_100U_UDP = 51;
        public static UInt32 ZCAN_CANFDNET_400U_TCP = 52;
        public static UInt32 ZCAN_CANFDNET_400U_UDP = 53;
        public static UInt32 ZCAN_CANFDNET_100U_TCP = 55;
        public static UInt32 ZCAN_CANFDNET_100U_UDP = 56;
        public static UInt32 ZCAN_CANFDNET_800U_TCP = 57;
        public static UInt32 ZCAN_CANFDNET_800U_UDP = 58;
        public static UInt32 ZCAN_USBCANFD_800U = 59;
        public static UInt32 ZCAN_PCIE_CANFD_100U_EX = 60;
        public static UInt32 ZCAN_PCIE_CANFD_400U_EX = 61;
        public static UInt32 ZCAN_PCIE_CANFD_200U_MINI = 62;
        public static UInt32 ZCAN_PCIE_CANFD_200U_EX = 63;
        public static UInt32 ZCAN_PCIE_CANFD_200U_M2 = 63;
        public static UInt32 ZCAN_CANFDDTU_400_TCP = 64;
        public static UInt32 ZCAN_CANFDDTU_400_UDP = 65;
        public static UInt32 ZCAN_CANFDWIFI_200U_TCP = 66;
        public static UInt32 ZCAN_CANFDWIFI_200U_UDP = 67;
        public static UInt32 ZCAN_CANFDDTU_800ER_TCP = 68;
        public static UInt32 ZCAN_CANFDDTU_800ER_UDP = 69;
        public static UInt32 ZCAN_CANFDDTU_800EWGR_TCP = 70;
        public static UInt32 ZCAN_CANFDDTU_800EWGR_UDP = 71;
        public static UInt32 ZCAN_CANFDDTU_600EWGR_TCP = 72;
        public static UInt32 ZCAN_CANFDDTU_600EWGR_UDP = 73;
        public static UInt32 ZCAN_CANFDDTU_CASCADE_TCP = 74;
        public static UInt32 ZCAN_CANFDDTU_CASCADE_UDP = 75;
        public static UInt32 ZCAN_USBCANFD_400U = 76;
        public static UInt32 ZCAN_CANFDDTU_200U = 77;
        public static UInt32 ZCAN_ZPSCANFD_TCP = 78;
        public static UInt32 ZCAN_ZPSCANFD_USB = 79;
        public static UInt32 ZCAN_CANFDBRIDGE_PLUS = 80;
        public static UInt32 ZCAN_CANFDDTU_300U = 81;
        public static UInt32 ZCAN_PCIE_CANFD_800U = 82;
        public static UInt32 ZCAN_PCIE_CANFD_1200U = 83;
        public static UInt32 ZCAN_MINI_PCIE_CANFD = 84;
        public static UInt32 ZCAN_USBCANFD_800H = 85;
        #endregion

        #region LIN 事件
        public static UInt32 ZCAN_LIN_WAKE_UP = 1;
        public static UInt32 ZCAN_LIN_ENTERED_SLEEP_MODE = 2;
        public static UInt32 ZCAN_LIN_EXITED_SLEEP_MODE = 3;
        #endregion

        #region 函数
        private const string DllName = "zlgcan.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern IntPtr ZCAN_OpenDevice(uint device_type, uint device_index, uint reserved);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_CloseDevice(IntPtr device_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern ZlgChannelHandle ZCAN_InitCAN(ZlgDeviceHandle device_handle, uint can_index, ref ZCAN_CHANNEL_INIT_CONFIG pInitConfig);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_StartCAN(ZlgChannelHandle chn_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ResetCAN(ZlgChannelHandle chn_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ClearBuffer(ZlgChannelHandle chn_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_GetReceiveNum(ZlgChannelHandle channel_handle, byte type);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_Transmit(ZlgChannelHandle channel_handle, ZCAN_Transmit_Data[] pTransmit, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_TransmitFD(ZlgChannelHandle channel_handle, ZCAN_TransmitFD_Data[] pTransmit, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_TransmitData(ZlgDeviceHandle device_handle, ZCANDataObj[] pTransmit, uint len);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_TransmitData(IntPtr device_handle, ZCANDataObj[] pTransmit, uint len);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_Receive(ZlgChannelHandle channel_handle, [In, Out] ZCAN_Receive_Data[] pReceive, uint len, int wait_time = -1);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ReceiveFD(ZlgChannelHandle channel_handle, [In, Out] ZCAN_ReceiveFD_Data[] pReceive, uint len, int wait_time = -1);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ReceiveData(ZlgDeviceHandle device_handle, [In, Out] ZCANDataObj[] pReceive, uint len, int wait_time);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ReceiveData(IntPtr device_handle, [In, Out] ZCANDataObj[] pReceive, uint len, int wait_time);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetValue(ZlgDeviceHandle device_handle, string path, string value);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetValue(IntPtr device_handle, string path, string value);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetValue(ZlgDeviceHandle device_handle, string path, IntPtr value);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetValue(IntPtr device_handle, string path, IntPtr value);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern IntPtr ZCAN_GetValue(IntPtr device_handle, string path);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern IntPtr ZCAN_GetValue(ZlgDeviceHandle device_handle, string path);

        // LIN
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern IntPtr ZCAN_InitLIN(ZlgDeviceHandle device_handle, uint lin_index, IntPtr pLINInitConfig);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_StartLIN(ZlgChannelHandle channel_handle);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ResetLIN(ZlgChannelHandle channel_handle);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_TransmitLIN(ZlgChannelHandle channel_handle, IntPtr pSend, uint Len);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_GetLINReceiveNum(ZlgChannelHandle channel_handle);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ReceiveLIN(ZlgChannelHandle channel_handle, IntPtr pReceive, uint Len, int WaitTime);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetLINPublish(ZlgChannelHandle channel_handle, IntPtr pSend, uint nPublishCount);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetLINSubscribe(ZlgChannelHandle channel_handle, IntPtr pSend, uint nSubscribeCount);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_WakeUpLIN(ZlgChannelHandle channel_handle);

        // UDS
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_UDS_Request(ZlgDeviceHandle device_handle, IntPtr req, IntPtr resp, IntPtr dataBuf, uint dataBufSize);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_UDS_Control(ZlgDeviceHandle device_handle, IntPtr ctrl, IntPtr resp);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_UDS_RequestEX(ZlgDeviceHandle device_handle, IntPtr requestData, IntPtr resp, IntPtr dataBuf, uint dataBufSize);
        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_UDS_ControlEX(ZlgDeviceHandle device_handle, uint dataType, IntPtr ctrl, IntPtr resp);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_IsDeviceOnLine(ZlgDeviceHandle device_handle);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ReadChannelErrInfo(ZlgChannelHandle channel_handle, out ZCAN_CHANNEL_ERROR_INFO pErrInfo);

           [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_ReadChannelStatus(ZlgChannelHandle channel_handle, out ZCAN_CHANNEL_STATUS  pCANStatus);
        #endregion

        #region 结构体
        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_CHANNEL_INIT_CONFIG
        {
            public uint can_type;               // TYPE_CAN / TYPE_CANFD
            public _ZCAN_CHANNEL_INIT_CONFIG config; // union { can; canfd; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct _ZCAN_CHANNEL_INIT_CONFIG
        {
            public _ZCAN_CHANNEL_CAN_INIT_CONFIG can;
            public _ZCAN_CHANNEL_CANFD_INIT_CONFIG canfd;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct _ZCAN_CHANNEL_CAN_INIT_CONFIG
        {
            public uint acc_code;
            public uint acc_mask;
            public uint reserved;
            public byte filter;
            public byte timing0;
            public byte timing1;
            public byte mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct _ZCAN_CHANNEL_CANFD_INIT_CONFIG
        {
            public uint acc_code;
            public uint acc_mask;
            public uint abit_timing;
            public uint dbit_timing;
            public uint brp;
            public byte filter;
            public byte mode;
            public ushort pad;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_Transmit_Data
        {
            public can_frame frame;
            public uint transmit_type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_Receive_Data
        {
            public can_frame frame;
            public UInt64 timestamp; // us
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_TransmitFD_Data
        {
            public canfd_frame frame;
            public uint transmit_type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_ReceiveFD_Data
        {
            public canfd_frame frame;
            public UInt64 timestamp; // us
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_AUTO_TRANSMIT_OBJ
        {
            public ushort enable;   // 0-禁用，1-使能
            public ushort index;    // 定时报文索引
            public uint interval;   // 周期 ms
            public ZCAN_Transmit_Data obj;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCANFD_AUTO_TRANSMIT_OBJ
        {
            public ushort enable;   // 0-禁用，1-使能
            public ushort index;    // 定时报文索引
            public uint interval;   // 周期 ms
            public ZCAN_TransmitFD_Data obj;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_CHANNEL_ERROR_INFO
        {
            public uint error_code;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] passive_ErrData;
            public byte arLost_ErrData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ZCAN_CHANNEL_STATUS
        {
            public byte errInterrupt;
            public byte regMode;
            public byte regStatus;
            public byte regALCapture;
            public byte regECCapture;
            public byte regEWLimit;
            public byte regRECounter;
            public byte regTECounter;
            public uint Reserved;
        }
        #endregion

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct can_frame
        {
            public uint can_id;
            public byte can_dlc;   // 0..8
            public byte __pad;
            public byte __res0;
            public byte __res1;
            public fixed byte data[8];
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct canfd_frame
        {
            public uint can_id;
            public byte len;
            public byte flags;
            public byte __res0;
            public byte __res1;
            public fixed byte data[64];
        }



        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANCANFDData
        {
            public UInt64 timeStamp;
            public UInt32 flag;          // bit 字段合集（见属性访问器）
            public fixed byte extraData[4];
            public canfd_frame frame;

            public uint frameType { get => (flag & 0x03); set => flag = (uint)((flag & ~0x03) | (value & 0x03)); }
            public uint txDelay   { get => (flag >> 2) & 0x03; set => flag = (uint)((flag & ~0x0C) | ((value & 0x03) << 2)); }
            public uint transmitType { get => (flag >> 4) & 0x0F; set => flag = (uint)((flag & ~0xF0) | ((value & 0x0F) << 4)); }
            public uint txEchoRequest { get => (flag >> 8) & 0x01; set => flag = (uint)((flag & ~0x100) | ((value & 0x01) << 8)); }
            public uint txEchoed { get => (flag >> 9) & 0x01; set => flag = (uint)((flag & ~0x200) | ((value & 0x01) << 9)); }
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANDataObj
        {
            public byte dataType;     // 1=CAN/CANFD, 4=LIN, 5=BusUsage 等
            public byte chnl;
            public UInt16 flag;       // 未使用
            public fixed byte extraData[4];
            public fixed byte data[92];
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_DYNAMIC_CONFIG_DATA
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public char[] key;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public char[] value;
        }



        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BusUsage
        {
            public UInt64 nTimeStampBegin;
            public UInt64 nTimeStampEnd;
            public byte   nChnl;
            public byte   nReserved;
            public ushort nBusUsage;   // *100 展示
            public uint   nFrameCount;
        }
    }
}
