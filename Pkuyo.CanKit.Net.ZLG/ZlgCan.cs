using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG
{
    /// <summary>
    /// ZLG family entry helpers for CAN (ZLG 系列设备的 CAN 入口封装)。
    /// </summary>
    public static class ZlgCan
    {
        /// <summary>
        /// Open a CAN session for a ZLG device type (为 ZLG 设备类型打开会话)。
        /// </summary>
        /// <param name="deviceType">ZLG device type (ZLG 设备类型)。</param>
        /// <param name="configure">Device init configurator, optional (设备初始化配置委托，可选)。</param>
        /// <returns>Opened <see cref="ZlgCanSession"/> (已打开的 ZLG 会话)。</returns>
        public static ZlgCanSession Open(this ZlgDeviceType deviceType,
            Action<ZlgDeviceInitOptionsConfigurator>? configure = null)
        {
            // 复用通用 Can.Open 逻辑，并指定具体设备与参数类型
            return (ZlgCanSession)Can.Open<ZlgCanDevice, ZlgCanChannel, ZlgDeviceOptions, ZlgDeviceInitOptionsConfigurator>(
                deviceType,
                configure,
                (device, provider) => new ZlgCanSession(device, provider));
        }
    }

    /// <summary>
    /// ZLG 专用会话封装，提供强类型通道创建能力 (ZLG 会话)。
    /// </summary>
    public class ZlgCanSession(ZlgCanDevice device, ICanModelProvider provider) : CanSession<ZlgCanDevice, ZlgCanChannel>(device, provider)
    {
        /// <summary>
        /// Create a ZLG channel bound to index (创建 ZLG 通道)。
        /// </summary>
        /// <param name="index">Channel index (通道索引)。</param>
        /// <param name="configure">Init configurator, optional (初始化配置委托，可选)。</param>
        /// <returns>ZLG channel (ZLG 通道)。</returns>
        public ZlgCanChannel CreateChannel(int index, Action<ZlgChannelInitConfigurator>? configure = null)
        {
            // 用户向的封装，复用通用 CAN 对应的类型与参数
            return CreateChannel<ZlgChannelOptions, ZlgChannelInitConfigurator>(index, configure);
        }
    }
}

