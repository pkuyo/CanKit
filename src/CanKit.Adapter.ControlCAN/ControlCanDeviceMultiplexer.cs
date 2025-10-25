using System;
using System.Collections.Concurrent;
using System.Threading;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ControlCAN;

internal static class ControlCanDeviceMultiplexer
{
    private static readonly ConcurrentDictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);
    private static string Key(DeviceType dt, uint index) => $"{dt.Id}|{index}";

    public static (ICanDevice device, IDisposable lease) Acquire(DeviceType dt, uint index, Func<ICanDevice> createAndOpen)
    {
        var key = Key(dt, index);
        while (true)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                Interlocked.Increment(ref existing.RefCount);
                return (existing.Device, new DeviceLease(key, existing));
            }

            var dev = createAndOpen();
            var entry = new Entry(dev);
            if (_map.TryAdd(key, entry))
            {
                return (entry.Device, new DeviceLease(key, entry));
            }
            try { dev.Dispose(); } catch { }
        }
    }

    private sealed class Entry
    {
        public int RefCount;
        public Entry(ICanDevice device) { Device = device; RefCount = 1; }
        public ICanDevice Device { get; }
    }

    private sealed class DeviceLease : IDisposable
    {
        private readonly string _key;
        private Entry? _entry;
        public DeviceLease(string key, Entry entry) { _key = key; _entry = entry; }
        public void Dispose()
        {
            var e = Interlocked.Exchange(ref _entry, null);
            if (e == null) return;
            var c = Interlocked.Decrement(ref e.RefCount);
            if (c > 0) return;
            if (_map.TryRemove(_key, out _))
            {
                try { e.Device.Dispose(); } catch { }
            }
        }
    }
}

