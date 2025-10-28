// Real PCAN-Basic interop (disabled in FAKE builds)
#if !FAKE
using System.Runtime.InteropServices;
using Peak.Can.Basic.BackwardCompatibility;


namespace CanKit.Adapter.PCAN.Native;
using TPCANHandle = UInt16;
using TPCANTimestampFD = UInt64;
internal static class PcanBasicNative
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TpcanMsg
    {
        public uint ID;
        [MarshalAs(UnmanagedType.U1)]
        public TPCANMessageType MSGTYPE;
        public byte LEN;
        public fixed byte DATA[16];
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct TpcanMsgFd
    {
        public uint ID;
        [MarshalAs(UnmanagedType.U1)]
        public TPCANMessageType MSGTYPE;
        public byte DLC;
        public fixed byte DATA[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TPCANTimestamp
    {
        public UInt32 millis;
        public UInt16 millis_overflow;
        public UInt16 micros;
    }

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_Write")]
    internal static unsafe extern TPCANStatus CAN_Write(
        TPCANHandle channel, TpcanMsg* message
    );

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_WriteFD")]
    internal static unsafe extern TPCANStatus CAN_WriteFD(
        TPCANHandle channel, TpcanMsgFd* message
    );

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_Read")]
    public static extern TPCANStatus CAN_Read(
        TPCANHandle Channel,
        out TPCANMsg Message,
        out TPCANTimestamp Timestamp);

    [DllImport("PCANBasic.dll", EntryPoint = "CAN_ReadFD")]
    public static extern TPCANStatus CAN_ReadFD(
        TPCANHandle Channel,
        out TPCANMsgFD Message,
        out TPCANTimestampFD Timestamp);

    public static TimeSpan ToTimeSpan(this TPCANTimestamp ts)
    {
        ulong totalUs =
            ts.micros +
            1000UL * ts.millis +
            0x100000000UL * 1000UL * ts.millis_overflow;

        return TimeSpan.FromTicks((long)totalUs * 10);
    }
}
#endif

