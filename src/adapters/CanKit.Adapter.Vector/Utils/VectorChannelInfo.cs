using System.Collections.Generic;
using CanKit.Core.Definitions;
namespace CanKit.Adapter.Vector.Utils;

internal record VectorChannelInfo
{
    public VectorChannelInfo(
        int globalChannelIndex,
        ulong channelMask,
        string name,
        uint hardwareType,
        uint hardwareIndex,
        uint hardwareChannel,
        string? transceiverName,
        uint channelCapabilities,
        uint channelBusCapabilities,
        uint connectedBusType,
        bool supportsCanFd)
    {
        GlobalChannelIndex = globalChannelIndex;
        ChannelMask = channelMask;
        Name = name;
        HardwareType = hardwareType;
        HardwareIndex = hardwareIndex;
        HardwareChannel = hardwareChannel;
        TransceiverName = transceiverName;
        ChannelCapabilities = channelCapabilities;
        ChannelBusCapabilities = channelBusCapabilities;
        ConnectedBusType = connectedBusType;
        SupportsCanFd = supportsCanFd;

        Capability = BuildCapability();
        AppName = string.Empty;
    }
    public string AppName { get; init; }
    public ulong ChannelMask { get; init; }

    public int AppChannelIndex { get; init; }

    public int GlobalChannelIndex { get; }
    public string Name { get; }

    public uint HardwareType { get; }
    public uint HardwareIndex { get; }
    public uint HardwareChannel { get; }
    public string? TransceiverName { get; }
    public uint ChannelCapabilities { get; }
    public uint ChannelBusCapabilities { get; }
    public uint ConnectedBusType { get; }
    public bool SupportsCanFd { get; }

    // Parsed capability (features + custom attributes) for this channel
    public Capability Capability { get; }

    private Capability BuildCapability()
    {
        var features = CanFeature.CanClassic; // channel list already filtered for CAN-compatible
        if (SupportsCanFd)
            features |= CanFeature.CanFd;

        // Vector XL API provides hardware acceptance filtering; enable Filters.
        features |= CanFeature.MaskFilter | CanFeature.RangeFilter;

        // XL driver reports error frames; expose ErrorFrame capability.
        features |= CanFeature.ErrorFrame;

        var custom = new Dictionary<string, object?>
        {
            { "xl_channel_caps", ChannelCapabilities },
            { "xl_bus_caps", ChannelBusCapabilities },
            { "xl_connected_bus", ConnectedBusType },
            { "xl_transceiver", TransceiverName },
            { "xl_hw_type", HardwareType },
            { "xl_hw_index", HardwareIndex },
            { "xl_hw_channel", HardwareChannel },
            { "xl_channel_name", Name },
            { "xl_global_channel_index", GlobalChannelIndex },
        };

        return new Capability(features, custom);
    }
}
