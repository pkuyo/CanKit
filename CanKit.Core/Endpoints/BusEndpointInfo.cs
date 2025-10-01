using System.Collections.Generic;
using CanKit.Core.Definitions;

namespace CanKit.Core.Endpoints;

/// <summary>
/// Describes a discoverable CAN endpoint that can be opened. (可被发现并能打开的 CAN 端点信息)
/// </summary>
public sealed class BusEndpointInfo
{
    /// <summary>
    /// Scheme name (e.g. "socketcan"/"pcan"/"zlg"). (协议名前缀)
    /// </summary>
    public string Scheme { get; init; } = string.Empty;

    /// <summary>
    /// Full endpoint string that can be passed to BusEndpointEntry.TryOpen. (可直接用于打开的完整 Endpoint 字符串)
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Optional display title. (用于展示的友好标题，可选)
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Device type if known. (若可用，对应的设备类型)
    /// </summary>
    public DeviceType? DeviceType { get; init; }

    /// <summary>
    /// Extra metadata such as interface, index, serial, etc. (扩展元数据)
    /// </summary>
    public IReadOnlyDictionary<string, string>? Meta { get; init; }

    public override string ToString() => Endpoint;
}

