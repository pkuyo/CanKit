using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Endpoints;
using Pkuyo.CanKit.PCAN.Definitions;

namespace Pkuyo.CanKit.Net.PCAN;

/// <summary>
/// Registers endpoint handler for scheme "pcan".
/// Examples:
///  - pcan://PCAN_USBBUS1
///  - pcan://?ch=PCAN_PCIBUS1
///  - pcan:PCAN_USBBUS2
/// </summary>
internal static class PcanEndpoint
{
    static PcanEndpoint()
    {
        BusEndpointRegistry.Register("pcan", Open);
    }

    private static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        string ch = ep.Path;
        if (string.IsNullOrWhiteSpace(ch))
        {
            if (ep.TryGet("ch", out var v) || ep.TryGet("channel", out v) || ep.TryGet("bus", out v))
                ch = v!;
        }

        if (string.IsNullOrWhiteSpace(ch))
        {
            // fallback to index -> USBBUS{index+1}
            int index = 0;
            if (int.TryParse(ep.Path, out var idx)) index = idx;
            ch = $"PCAN_USBBUS{Math.Max(1, index + 1)}";
        }

        return CanBus.Open<PcanBus, PcanBusOptions, PcanBusInitConfigurator>(
            PcanDeviceType.PCANBasic,
            0,
            cfg =>
            {
                cfg.UseChannel(ch);
                configure?.Invoke(cfg);
            });
    }
}

