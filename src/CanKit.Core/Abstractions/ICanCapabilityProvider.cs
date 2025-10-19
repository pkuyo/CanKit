using CanKit.Core.Definitions;

namespace CanKit.Core.Abstractions;

/// <summary>
/// Interface for provider-level capability sniffing without opening a bus.
/// 由 Provider 直接嗅探能力的接口。
/// </summary>
public interface ICanCapabilityProvider
{
    /// <summary>
    /// Sniff capabilities based on prepared device/channel configuration.
    /// 基于已构造的设备/通道配置执行能力嗅探。
    /// </summary>
    /// <param name="busOptions"></param>
    /// <returns>Capability report including built-in and custom features.</returns>
    Capability QueryCapabilities(IBusOptions busOptions);
}

