using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.SocketCAN.Definitions;

public static class LinuxDeviceType
{
    public static readonly DeviceType SocketCAN = DeviceType.Register("Linux.SocketCAN", 0);
}

