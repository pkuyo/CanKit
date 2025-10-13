using System.Runtime.InteropServices;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Native;

internal static class CanlibNative
{
    [DllImport("canlib32")]
    public static unsafe extern Canlib.canStatus canWrite(
        int hnd,
        int id,
        void* msg,
        uint dlc,
        uint flag);
}
