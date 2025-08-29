using System;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    public interface ICanDevice : IDisposable
    {

        bool OpenDevice();

        void CloseDevice();

        IntPtr NativePtr { get; }

        bool IsDeviceOpen { get; }
    }
}
