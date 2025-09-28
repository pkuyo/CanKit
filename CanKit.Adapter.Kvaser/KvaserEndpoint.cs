using CanKit.Adapter.Kvaser.Definitions;
using CanKit.Core;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Endpoints;

namespace CanKit.Adapter.Kvaser;

/// <summary>
/// Registers endpoint handler for scheme "kvaser".
/// Examples:
///  - kvaser://0          (open by channel number)
///  - kvaser://?ch=1      (open by channel number)
///  - kvaser:1            (fallback form)
/// </summary>
[CanEndPoint("kvaser")]
internal static class KvaserEndpoint
{
    public static ICanBus Open(CanEndpoint ep, Action<IBusInitOptionsConfigurator>? configure)
    {
        int channel = 0;
        if (!string.IsNullOrWhiteSpace(ep.Path))
        {
            _ = int.TryParse(ep.Path, out channel);
        }
        if (channel == 0)
        {
            if (ep.TryGet("ch", out var v) || ep.TryGet("channel", out v) || ep.TryGet("bus", out v))
            {
                _ = int.TryParse(v, out channel);
            }
        }

        return CanBus.Open<KvaserBus, KvaserBusOptions, KvaserBusInitConfigurator>(
            KvaserDeviceType.CANlib,
            0,
            cfg =>
            {
                cfg.UseChannelNumber(channel);
                configure?.Invoke(cfg);
            });
    }
}
