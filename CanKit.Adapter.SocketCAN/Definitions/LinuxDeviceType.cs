using CanKit.Core.Definitions;

namespace CanKit.Adapter.SocketCAN.Definitions;

public static class LinuxDeviceType
{
    public static readonly DeviceType SocketCAN = DeviceType.Register("Linux.SocketCAN", 0);
}

