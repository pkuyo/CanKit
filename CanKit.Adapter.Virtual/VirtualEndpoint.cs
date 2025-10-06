using System;
using System.Collections.Generic;
using CanKit.Adapter.Virtual.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;

namespace CanKit.Adapter.Virtual;

/// <summary>
/// Registers endpoint handler for scheme "virtual".
/// Format: virtual://sessionId/channelId
/// - sessionId: any string (grouped into same bus hub)
/// - channelId: integer (>= 0)
/// </summary>
[CanEndPoint("virtual")]
internal static class VirtualEndpoint
{
    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        // parse path: "sessionId/channelId"
        string session = ep.Path;
        int channel = 0;
        if (!string.IsNullOrWhiteSpace(session))
        {
            var parts = session.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) session = parts[0];
            if (parts.Length > 1) int.TryParse(parts[1], out channel);
        }

        if (string.IsNullOrWhiteSpace(session)) session = "default";

        return CanBus.Open<VirtualBus, VirtualBusOptions, VirtualBusInitConfigurator>(
            VirtualDeviceType.Virtual,
            cfg =>
            {
                cfg.UseSession(session)
                   .UseChannelIndex(channel)
                   .UseChannelName($"virtual{channel}");
                configure?.Invoke(cfg);
            });
    }

    /// <summary>
    /// Enumerate two sessions with three channels each for sniffing.
    /// Also supports arbitrary session/channel via Open.
    /// </summary>
    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        var sessions = new[] { "alpha", "beta" };
        foreach (var s in sessions)
        {
            for (int ch = 0; ch < 3; ch++)
            {
                yield return new BusEndpointInfo
                {
                    Scheme = "virtual",
                    Endpoint = $"virtual://{s}/{ch}",
                    Title = $"{s} ch{ch} (Virtual)",
                    DeviceType = VirtualDeviceType.Virtual,
                    Meta = new Dictionary<string, string>
                    {
                        { "session", s },
                        { "channel", ch.ToString() }
                    }
                };
            }
        }
    }
}
