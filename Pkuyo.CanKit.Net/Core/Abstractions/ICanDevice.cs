using System;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Core.Abstractions
{
    /// <summary>
    ///     表示一个 CAN 物理设备，负责与硬件适配器交互。
    /// </summary>
    public interface ICanDevice : IDisposable
    {

        /// <summary>
        ///     打开设备资源并做好进行通道操作的准备。
        /// </summary>
        /// <returns>打开设备是否成功。</returns>
        void OpenDevice();

        /// <summary>
        ///     关闭设备连接并释放相关资源。
        /// </summary>
        void CloseDevice();

        /// <summary>
        ///     获取设备当前是否已处于打开状态。
        /// </summary>
        bool IsDeviceOpen { get; }

        /// <summary>
        ///     获取用于访问运行时设备参数的配置器。
        /// </summary>
        IDeviceRTOptionsConfigurator Options { get; }
    }

    /// <summary>
    ///     强类型化运行时配置访问器的设备接口。
    /// </summary>
    /// <typeparam name="TConfigurator">设备运行时配置器的具体类型。</typeparam>
    public interface ICanDevice<out TConfigurator> : ICanDevice
        where TConfigurator : IDeviceRTOptionsConfigurator
    {
        /// <summary>
        ///     获取强类型的运行时配置访问器。
        /// </summary>
        new TConfigurator Options { get; }
    }
}
