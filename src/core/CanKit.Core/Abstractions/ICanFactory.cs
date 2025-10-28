using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions;

/// <summary>
/// Factory to create CAN components (创建 CAN 组件的工厂)。
/// </summary>
public interface ICanFactory
{
    /// <summary>
    /// Create a device with provided options (用给定选项创建设备)。
    /// </summary>
    /// <param name="options">Device initialization options (设备初始化选项)。</param>
    /// <returns>Created device (创建设备)。</returns>
    ICanDevice CreateDevice(IDeviceOptions options);

    /// <summary>
    /// Create a bus for the device (为设备创建总线通道)。
    /// </summary>
    /// <param name="device">Device instance (设备实例)。</param>
    /// <param name="options">Channel initialization options (通道初始化选项)。</param>
    /// <param name="transceiver">Transceiver to use (收发器)。</param>
    /// <param name="provider"></param>
    /// <returns>Created bus (创建的总线)。</returns>
    ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider);

    /// <summary>
    /// Create a transceiver matching given options (根据选项创建匹配的收发器)。
    /// </summary>
    /// <param name="deviceOptions">Device RT options configurator (设备运行时配置器)。</param>
    /// <param name="busOptions">Channel init options configurator (通道初始化配置器)。</param>
    /// <returns>Transceiver instance (收发器实例)。</returns>
    ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions,
        IBusInitOptionsConfigurator busOptions);

    /// <summary>
    /// Whether this factory supports the device type (是否支持该设备类型)。
    /// </summary>
    /// <param name="deviceType">Device type to check (待检查的设备类型)。</param>
    /// <returns><c>true</c> if supported; otherwise <c>false</c> (支持返回 true)。</returns>
    bool Support(DeviceType deviceType);
}

