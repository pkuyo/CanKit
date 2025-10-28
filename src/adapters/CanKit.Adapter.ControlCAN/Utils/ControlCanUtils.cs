using System;
using System.Runtime.CompilerServices;
using CanKit.Core.Definitions;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN.Utils;

internal static class ControlCanUtils
{
    public static uint ToCanID(this in CanClassicFrame frame)
    {
        var id = (uint)frame.ID;
        var cid = frame.IsExtendedFrame ? ((id & CcApi.CAN_EFF_MASK) | CcApi.CAN_EFF_FLAG)
            : (id & CcApi.CAN_SFF_MASK);
        if (frame.IsRemoteFrame) cid |= CcApi.CAN_RTR_FLAG;
        if (frame.IsErrorFrame) cid |= CcApi.CAN_ERR_FLAG;
        return cid;
    }

    public static CanClassicFrame FromNative(this in CcApi.VCI_CAN_OBJ obj)
    {
        var data = new byte[obj.DataLen];

        unsafe
        {
            fixed (byte* dst = data)
            fixed (byte* src = obj.Data)
            {
                Unsafe.CopyBlockUnaligned(dst, src, obj.DataLen);
            }
        }

        return new CanClassicFrame((int)(obj.ExternFlag == 1
                ? obj.ID & CcApi.CAN_EFF_MASK : obj.ID & CcApi.CAN_SFF_MASK),
            data, obj.ExternFlag == 1, obj.RemoteFlag == 1);
    }

    public static unsafe void ToNative(this in CanClassicFrame f, CcApi.VCI_CAN_OBJ* pObj, bool retry)
    {
        var dataLen = Math.Min(f.Dlc, (byte)8);
        var span = f.Data.Span;
        fixed (byte* ptr = span)
        {
            Unsafe.CopyBlockUnaligned(pObj->Data, ptr, dataLen);
        }

        pObj->ID = f.ToCanID();
        pObj->DataLen = dataLen;
        pObj->ExternFlag = (byte)(f.IsExtendedFrame ? 1 : 0);
        pObj->RemoteFlag = (byte)(f.IsRemoteFrame ? 1 : 0);
        pObj->SendType =  (byte)(retry ? 0 : 1);
    }
}
