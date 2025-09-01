using System;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    public interface ICanDevice : IDisposable
    {

        bool OpenDevice();

        void CloseDevice();

        IntPtr NativePtr { get; }

        bool IsDeviceOpen { get; }
    }

    public interface ICanDevice<out TConfigurator> : ICanDevice
        where TConfigurator : IDeviceRTOptionsConfigurator<IDeviceOptions>
    {
        TConfigurator Options { get; }
    }
}
