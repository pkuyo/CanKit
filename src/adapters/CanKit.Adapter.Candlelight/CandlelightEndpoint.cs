using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.Candlelight.Native;
using CanKit.Core.Registry;

namespace CanKit.Adapter.Candlelight;

internal static class CandlelightEndpoint
{
    public static PreparedBusContext Prepare(CanEndpoint endpoint, Action<IBusInitOptionsConfigurator>? configure)
    {
        var parts = endpoint.Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2) throw new FormatException("Candlelight endpoint must be candlelight://device/channel.");
        uint device = 0;
        int channel = 0;
        if (parts.Length > 0 && !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out device))
            throw new FormatException("Invalid Candlelight device index.");
        if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out channel))
            throw new FormatException("Invalid Candlelight channel index.");
        if (!string.IsNullOrEmpty(endpoint.Fragment))
        {
            var fragment = endpoint.Fragment!;
            if (fragment.StartsWith("ch", StringComparison.OrdinalIgnoreCase)) fragment = fragment.Substring(2);
            if (!int.TryParse(fragment, NumberStyles.None, CultureInfo.InvariantCulture, out channel))
                throw new FormatException("Invalid Candlelight channel fragment.");
        }
        var provider = CanRegistry.Registry.Resolve(CandlelightDeviceType.Candlelight);
        var (deviceOptions, deviceConfigurator) = provider.GetDeviceOptions();
        ((CandlelightDeviceOptions)deviceOptions).DeviceIndex = device;
        var (busOptions, busConfigurator) = provider.GetChannelOptions();
        busConfigurator.UseChannelIndex(channel);
        configure?.Invoke(busConfigurator);
        return new PreparedBusContext(provider, deviceOptions, deviceConfigurator, busOptions, busConfigurator);
    }

    public static ICanBus Open(CanEndpoint endpoint, Action<IBusInitOptionsConfigurator>? configure)
    {
        var (provider, deviceOptions, _, busOptions, busConfigurator) = Prepare(endpoint, configure);
        var device = provider.Factory.CreateDevice(deviceOptions);
        try
        {
            var transceiver = provider.Factory.CreateTransceivers(device.Options, busConfigurator);
            var bus = provider.Factory.CreateBus(device, busOptions, transceiver, provider);
            if (bus is not IOwnership ownership)
                throw new InvalidOperationException("Candlelight bus does not support device ownership.");
            ownership.AttachOwner(device);
            return bus;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public static IEnumerable<BusEndpointInfo> Enumerate()
    {
        var result = new List<BusEndpointInfo>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return result;
        IntPtr list = IntPtr.Zero;
        try
        {
            if (!CandleNative.candle_list_scan(out list) || !CandleNative.candle_list_length(list, out var count))
                return result;
            for (int i = 0; i < count; i++)
            {
                if (!CandleNative.candle_dev_get(list, (byte)i, out var device)) continue;
                try
                {
                    var path = Marshal.PtrToStringUni(CandleNative.candle_dev_get_path(device));
                    var meta = new Dictionary<string, string> { ["deviceIndex"] = i.ToString(CultureInfo.InvariantCulture) };
                    if (!string.IsNullOrEmpty(path)) meta["path"] = path!;
                    if (CandleNative.candle_dev_get_state(device, out var state)) meta["state"] = state == 0 ? "available" : "in-use";
                    result.Add(new BusEndpointInfo
                    {
                        Scheme = "candlelight",
                        Endpoint = $"candlelight://{i}/0",
                        Title = $"Candlelight device {i} ch0",
                        DeviceType = CandlelightDeviceType.Candlelight,
                        Meta = meta
                    });
                }
                finally { CandleNative.candle_dev_free(device); }
            }
        }
        catch (DllNotFoundException) { }
        catch (BadImageFormatException) { }
        catch (EntryPointNotFoundException) { }
        finally { if (list != IntPtr.Zero) CandleNative.candle_list_free(list); }
        return result;
    }
}
