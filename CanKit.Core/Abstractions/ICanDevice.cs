using System;

namespace CanKit.Core.Abstractions
{
    /// <summary>
    /// Represents a CAN device (表示一个 CAN 设备)，封装底层硬件的打开/关闭与运行时选项。
    /// </summary>
    public interface ICanDevice : IDisposable
    {
        /// <summary>
        /// Whether device is open (设备是否已打开)。
        /// </summary>
        bool IsDeviceOpen { get; }

        /// <summary>
        /// Real-time device options configurator (运行时设备选项配置器)。
        /// </summary>
        IDeviceRTOptionsConfigurator Options { get; }

        /// <summary>
        /// Open device and prepare channels (打开设备并准备通道)。
        /// </summary>
        void OpenDevice();

        /// <summary>
        /// Close device and release resources (关闭设备并释放资源)。
        /// </summary>
        void CloseDevice();
    }

    /// <summary>
    /// Device interface with strong-typed configurator (带强类型运行时配置器的设备接口)。
    /// </summary>
    /// <typeparam name="TConfigurator">Configurator type (配置器类型)。</typeparam>
    public interface ICanDevice<out TConfigurator> : ICanDevice
        where TConfigurator : IDeviceRTOptionsConfigurator
    {
        /// <summary>
        /// Strong-typed RT options (强类型运行时选项)。
        /// </summary>
        new TConfigurator Options { get; }
    }
}

