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
        => Open(endpoint, configure, ResolveDevicePath);

    internal static ICanBus Open(CanEndpoint endpoint, Action<IBusInitOptionsConfigurator>? configure, Func<uint, string> resolveDevicePath)
    {
        var (provider, deviceOptions, _, busOptions, busConfigurator) = Prepare(endpoint, configure);
        var options = (CandlelightDeviceOptions)deviceOptions;
        var path = resolveDevicePath(options.DeviceIndex);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Candle device index {options.DeviceIndex} has no stable device path.");
        options.DevicePath = path;
        var (device, lease) = CandlelightDeviceMultiplexer.Acquire(
            path,
            () => provider.Factory.CreateDevice(deviceOptions));
        try
        {
            var transceiver = provider.Factory.CreateTransceivers(device.Options, busConfigurator);
            var bus = provider.Factory.CreateBus(device, busOptions, transceiver, provider);
            if (bus is not IOwnership ownership)
                throw new InvalidOperationException("Candlelight bus does not support device ownership.");
            ownership.AttachOwner(lease);
            return bus;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static string ResolveDevicePath(uint index)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Candle.dll is supported only on Windows.");
        if (index > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(index));

        var api = CandleApi.Instance;
        IntPtr list = IntPtr.Zero;
        IntPtr device = IntPtr.Zero;
        try
        {
            if (!api.Scan(out list))
                throw new InvalidOperationException("Candle device scan failed.");
            if (!api.ListLength(list, out var count) || index >= count)
                throw new InvalidOperationException($"Candle device index {index} is not available.");
            if (!api.GetDevice(list, (byte)index, out device))
                throw new InvalidOperationException($"Could not retrieve Candle device {index}.");
            return api.GetPath(device) is { Length: > 0 } path
                ? path
                : throw new InvalidOperationException($"Candle device index {index} has no stable device path.");
        }
        finally
        {
            if (device != IntPtr.Zero) api.FreeDevice(device);
            if (list != IntPtr.Zero) api.FreeList(list);
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
