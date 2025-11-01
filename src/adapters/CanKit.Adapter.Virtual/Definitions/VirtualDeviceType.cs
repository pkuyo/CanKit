using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual.Definitions;

public static class VirtualDeviceType
{
    // Pure in-process virtual bus for testing
    public static readonly DeviceType Virtual = DeviceType.Register("Virtual.Bus");
}

