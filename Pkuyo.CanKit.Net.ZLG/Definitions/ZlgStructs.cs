using Pkuyo.CanKit.Net.Core.Definitions;

namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
}

namespace Pkuyo.CanKit.ZLG.Definitions
{
    public readonly record struct CanDeviceInfo(ZlgDeviceKind DeviceType, uint DeviceIndex);
}