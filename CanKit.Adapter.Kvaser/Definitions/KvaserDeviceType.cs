using CanKit.Core.Definitions;

namespace CanKit.Adapter.Kvaser.Definitions;

public static class KvaserDeviceType
{
    // Kvaser CANlib compatible devices (Windows/Linux with CANlib SDK)
    public static readonly DeviceType CANlib = DeviceType.Register("Kvaser.CANlib", 0);
}

