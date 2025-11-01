// FAKE PcanBasicNative for unit tests
#if FAKE
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using Peak.Can.Basic;
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

    public static TimeSpan ToTimeSpan(this TPCANTimestamp ts)
    {
        ulong totalUs = ts.micros + 1000UL * ts.millis + 0x100000000UL * 1000UL * ts.millis_overflow;
        return TimeSpan.FromTicks((long)totalUs * 10);
    }

    public static unsafe TPCANStatus CAN_Write(TPCANHandle channel, TpcanMsg* message)
    {
        try
        {
            var id = message->ID;
            var type = message->MSGTYPE;
            var len = Math.Min(8, (int)message->LEN);
            var data = new byte[len];
            for (int i = 0; i < len; i++) data[i] = message->DATA[i];
            Api.Submit((PcanChannel)channel, id, type, data, isFd: false);
            return TPCANStatus.PCAN_ERROR_OK;
        }
        catch
        {
            return 0; // OK
        }
    }

    public static unsafe TPCANStatus CAN_WriteFD(TPCANHandle channel, TpcanMsgFd* message)
    {
        try
        {
            var id = message->ID;
            var type = message->MSGTYPE;
            int len = Math.Min(64, CanFrame.DlcToLen(message->DLC));
            var data = new byte[len];
            for (int i = 0; i < len; i++) data[i] = message->DATA[i];
            Api.Submit((PcanChannel)channel, id, type, data, isFd: true);
            return TPCANStatus.PCAN_ERROR_OK;
        }
        catch
        {
            return 0; // OK
        }
    }

    public static TPCANStatus CAN_Read(TPCANHandle Channel, out TpcanMsg Message, out TPCANTimestamp Timestamp)
    {
        if (Api.TryDequeue((PcanChannel)Channel, out var msg, out uint micros))
        {
            Message = msg;
            Timestamp = new TPCANTimestamp
            {
                millis = micros / 1000,
                micros = (ushort)(micros % 1000),
                millis_overflow = 0
            };
            return TPCANStatus.PCAN_ERROR_OK;
        }
        Message = default; Timestamp = default;
        return TPCANStatus.PCAN_ERROR_QRCVEMPTY;
    }

    public static TPCANStatus CAN_ReadFD(TPCANHandle Channel, out TpcanMsgFd Message, out TPCANTimestampFD Timestamp)
    {
        if (Api.TryDequeueFd((PcanChannel)Channel, out var msg, out ulong micros))
        {
            Message = msg;
            Timestamp = micros;
            return TPCANStatus.PCAN_ERROR_OK;
        }
        Message = default; Timestamp = 0;
        return TPCANStatus.PCAN_ERROR_QRCVEMPTY;
    }
}

#endif

