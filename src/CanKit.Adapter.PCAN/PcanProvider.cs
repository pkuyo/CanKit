using CanKit.Adapter.PCAN.Definitions;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Registry;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN;

public sealed class PcanProvider : ICanModelProvider, ICanCapabilityProvider
{
    public DeviceType DeviceType => PcanDeviceType.PCANBasic;

    // FD depends on hardware (sniffed at runtime).
    public CanFeature StaticFeatures => CanFeature.CanClassic |
                                        CanFeature.CanFd |
                                        CanFeature.Filters |
                                        CanFeature.ErrorFrame |
                                        CanFeature.ListenOnly;

    public ICanFactory Factory => CanRegistry.Registry.Factory("PCAN");

    public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
    {
        var options = new NullDeviceOptions(this);
        var cfg = new NullDeviceInitOptionsConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }

    public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
    {
        var options = new PcanBusOptions(this)
        {
            BitTiming = CanBusTiming.ClassicDefault(),
            ProtocolMode = CanProtocolMode.Can20,
            WorkMode = ChannelWorkMode.Normal,
            ChannelName = $"PCAN_USBBUS1"
        };
        var cfg = new PcanBusInitConfigurator();
        cfg.Init(options);
        return (options, cfg);
    }


    public Capability QueryCapabilities(IBusOptions busOptions)
    {
        var handle = ParseHandle(busOptions.ChannelName!);
        return QueryCapabilities(handle, busOptions.Features);
    }

    internal static Capability QueryCapabilities(PcanChannel handle, CanFeature originFeatures)
    {
        // Query channel features and removed not-supported ones
        if (Api.GetValue(handle, PcanParameter.ChannelFeatures, out uint feature) == PcanStatus.OK)
        {
            var feats = (PcanDeviceFeatures)feature;
            CanFeature dyn = originFeatures;
            if ((feats & PcanDeviceFeatures.FlexibleDataRate) == 0)
            {
                dyn &= ~CanFeature.CanFd;
            }

            originFeatures = dyn;
        }
        else
        {
            CanKitLogger.LogWarning($"PCAN: PcanBus get channel features failed. Channel={handle}");
        }

        return new(originFeatures);
    }

    internal static PcanChannel ParseHandle(string s)
    {
        s = s.Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            throw new CanBusCreationException("PCAN channel must not be empty.");
        }

        // PcanChannel.NoneBus
        if (s.Equals("PCAN_NONEBUS", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("NONEBUS", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            s == "0")
            return 0;

        if (IsAllDigits(s))
        {
            if (int.TryParse(s, out var raw) && Enum.IsDefined(typeof(PcanChannel), raw))
                return (PcanChannel)raw;
            throw new CanBusCreationException($"Unknown PCAN channel value '{s}'.");
        }

        // Enum names: Usb01, Pci02
        if (Enum.TryParse<PcanChannel>(s, ignoreCase: true, out var named))
            return named;

        // PCAN names: PCAN_USBBUSn, PCAN_PCIBUSn, PCAN_LANBUSn
        var upper = s.ToUpperInvariant();

        PcanChannel FromIndex(string kind, int idx)
        {
            if (idx <= 0)
                throw new CanBusCreationException($"Channel index must start from 1 for {kind} (got {idx}).");
            var name = kind + idx.ToString("00"); // Usb01 / Pci02 / Lan12
            if (Enum.TryParse<PcanChannel>(name, ignoreCase: true, out var ch))
                return ch;
            throw new CanBusCreationException($"Unknown PCAN channel '{s}'.");
        }

        var m = System.Text.RegularExpressions.Regex.Match(upper, @"^(?:PCAN_)?USB(?:BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var usbN))
            return FromIndex("Usb", usbN);

        m = System.Text.RegularExpressions.Regex.Match(upper, @"^(?:PCAN_)?PCI(?:BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var pciN))
            return FromIndex("Pci", pciN);

        m = System.Text.RegularExpressions.Regex.Match(upper, @"^(?:PCAN_)?LAN(?:BUS)?(?<n>\d+)$");
        if (m.Success && int.TryParse(m.Groups["n"].Value, out var lanN))
            return FromIndex("Lan", lanN);

        throw new CanBusCreationException($"Unknown PCAN channel '{s}'.");

        static bool IsAllDigits(string t)
        {
            if (t.Any(ch => ch is < '0' or > '9'))
            {
                return false;
            }
            return t.Length > 0;
        }
    }

}
