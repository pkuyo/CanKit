using System;
using System.Collections.Generic;
using System.Text;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Devices;
using ZlgCAN.Net.Core.Models;

namespace ZlgCAN.Net.Core.Factory
{
    public static class CanDeviceFactory
    {
        public static ICanDevice Create(DeviceType deviceType, uint deviceIndex)
        {
            switch (deviceType)
            {
                case DeviceType.ZCAN_USBCAN1:
                case DeviceType.ZCAN_USBCAN2:
                    return new UsbCanDevice(new CanDeviceInfo()
                    {
                        DeviceType = deviceType,
                        DeviceIndex = deviceIndex,
                    });
                default:
                    throw new NotSupportedException($"Device type {deviceType} is not supported.");
            }
        }
    }
}
