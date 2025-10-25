// Real ControlCAN interop (disabled in FAKE builds)
#if !FAKE
using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ControlCAN.Definitions;

namespace CanKit.Adapter.ControlCAN.Native;

internal static class ControlCAN
{
    public const uint CAN_EFF_FLAG = 0x80000000U; // extended frame format
    public const uint CAN_RTR_FLAG = 0x40000000U; // remote transmission request
    public const uint CAN_ERR_FLAG = 0x20000000U; // error frame flag

    public const uint CAN_SFF_MASK = 0x000007FFU; // standard frame format mask
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU; // extended frame format mask

    private const string DllName = "controlcan";
    public const int BATCH_COUNT = 64;
    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_BOARD_INFO
    {
        public ushort hw_Version;
        public ushort fw_Version;
        public ushort dr_Version;
        public ushort in_Version;
        public ushort irq_Num;
        public byte can_Num;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)] public byte[] str_Serial;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)] public byte[] str_hw_Type;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public ushort[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct VCI_CAN_OBJ
    {
        public uint ID;
        public uint TimeStamp;
        public byte TimeFlag;
        public byte SendType;
        public byte RemoteFlag;
        public byte ExternFlag;
        public byte DataLen;
        public fixed byte Data[8];
        public fixed byte Reserved[3];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_INIT_CONFIG
    {
        public uint AccCode;
        public uint AccMask;
        public uint Reserved;
        public byte Filter;
        public byte Timing0;
        public byte Timing1;
        public byte Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VCI_ERR_INFO
    {
        public uint ErrCode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] Passive_ErrData;
        public byte ArLost_ErrData;
    }

    [DllImport(DllName)]
    public static extern uint VCI_OpenDevice(uint DevType, uint DevIndex, uint Reserved);

    [DllImport(DllName)]
    public static extern uint VCI_CloseDevice(uint DevType, uint DevIndex);

    [DllImport(DllName)]
    public static extern uint VCI_InitCAN(uint DevType, uint DevIndex, uint CANIndex, ref VCI_INIT_CONFIG pInitConfig);

    [DllImport(DllName)]
    public static extern uint VCI_StartCAN(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static extern uint VCI_ResetCAN(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static extern uint VCI_ClearBuffer(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static unsafe extern uint VCI_Transmit(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ* pSend, uint Len);

    [DllImport(DllName)]
    public static extern uint VCI_Receive(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ[] pReceive, uint Len, int WaitTime);

    [DllImport(DllName)]
    public static extern uint VCI_GetReceiveNum(uint DevType, uint DevIndex, uint CANIndex);

    [DllImport(DllName)]
    public static extern uint VCI_ReadErrInfo(uint DevType, uint DevIndex, uint CANIndex, out VCI_ERR_INFO pErrInfo);

    // Some advanced device types support pre/post init configuration via SetReference.
    // RefType and data layout follow vendor documentation.
    [DllImport(DllName)]
    public static extern uint VCI_SetReference(uint DevType, uint DevIndex, uint CANIndex, uint RefType, IntPtr pData);
}

#else
// FAKE fallback for environments without controlcan.dll
using System;
using CanKit.Adapter.ControlCAN.Definitions;

namespace CanKit.Adapter.ControlCAN.Native;

internal static class ControlCAN
{
    public const uint CAN_EFF_FLAG = 0x80000000U; // extended frame format
    public const uint CAN_RTR_FLAG = 0x40000000U; // remote transmission request
    public const uint CAN_ERR_FLAG = 0x20000000U; // error frame flag

    public const uint CAN_SFF_MASK = 0x000007FFU; // standard frame format mask
    public const uint CAN_EFF_MASK = 0x1FFFFFFFU; // extended frame format mask
    public const int BATCH_COUNT = 64;
    public struct VCI_BOARD_INFO { }
    public struct VCI_CAN_OBJ
    {
        public uint ID;
        public uint TimeStamp;
        public byte TimeFlag;
        public byte SendType;
        public byte RemoteFlag;
        public byte ExternFlag;
        public byte DataLen;
        public byte[] Data;
        public byte[] Reserved;
    }
    public struct VCI_INIT_CONFIG
    {
        public uint AccCode;
        public uint AccMask;
        public uint Reserved;
        public byte Filter;
        public byte Timing0;
        public byte Timing1;
        public byte Mode;
    }
    public struct VCI_ERR_INFO
    {
        public uint ErrCode;
        public byte[] Passive_ErrData;
        public byte ArLost_ErrData;
    }

    public static uint VCI_OpenDevice(uint DevType, uint DevIndex, uint Reserved) => 1;
    public static uint VCI_CloseDevice(uint DevType, uint DevIndex) => 1;
    public static uint VCI_InitCAN(uint DevType, uint DevIndex, uint CANIndex, ref VCI_INIT_CONFIG pInitConfig) => 1;
    public static uint VCI_StartCAN(uint DevType, uint DevIndex, uint CANIndex) => 1;
    public static uint VCI_ResetCAN(uint DevType, uint DevIndex, uint CANIndex) => 1;
    public static uint VCI_ClearBuffer(uint DevType, uint DevIndex, uint CANIndex) => 1;
    public static unsafe extern uint VCI_Transmit(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ* pSend, uint Len) => Len;
    public static unsafe extern uint VCI_Receive(uint DevType, uint DevIndex, uint CANIndex, VCI_CAN_OBJ[] pReceive, uint Len, int WaitTime) => 0;
    public static uint VCI_GetReceiveNum(uint DevType, uint DevIndex, uint CANIndex) => 0;
    public static uint VCI_ReadErrInfo(uint DevType, uint DevIndex, uint CANIndex, out VCI_ERR_INFO pErrInfo)
    {
        pErrInfo = new VCI_ERR_INFO();
        return 1;
    }

    public static uint VCI_SetReference(uint DevType, uint DevIndex, uint CANIndex, uint RefType, IntPtr pData) => 1;
}
#endif
