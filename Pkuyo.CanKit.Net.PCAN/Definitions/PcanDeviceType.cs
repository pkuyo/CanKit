using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.PCAN.Definitions;

public static class PcanDeviceType
{
    // Single device family for Peak PCAN using PCAN-Basic backend
    public static readonly DeviceType PCANBasic = DeviceType.Register("Windows.PCAN", 0);
}

