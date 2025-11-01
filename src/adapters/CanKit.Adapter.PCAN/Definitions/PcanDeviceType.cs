using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.PCAN.Definitions;

public static class PcanDeviceType
{
    // Single device family for Peak PCAN using PCAN-Basic backend
    public static readonly DeviceType PCANBasic = DeviceType.Register("Windows.PCAN");
}
