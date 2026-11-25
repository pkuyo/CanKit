using System;
using System.Collections.Generic;
using System.Threading;
using CanKit.Abstractions.API.Can;

namespace CanKit.Adapter.Candlelight;

internal static class CandlelightDeviceMultiplexer
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Devices = new(StringComparer.Ordinal);

    public static (ICanDevice device, IDisposable lease) Acquire(string path, Func<ICanDevice> createAndOpen)
    {
        lock (Gate)
        {
            if (!Devices.TryGetValue(path, out var entry))
            {
                entry = new Entry(createAndOpen());
                Devices.Add(path, entry);
            }
            entry.RefCount++;
            return (entry.Device, new DeviceLease(path, entry));
        }
    }

    private sealed class Entry
    {
        public Entry(ICanDevice device) => Device = device;
        public ICanDevice Device { get; }
        public int RefCount;
    }

    private sealed class DeviceLease : IDisposable
    {
        private readonly string _path;
        private Entry? _entry;

        public DeviceLease(string path, Entry entry) { _path = path; _entry = entry; }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry == null) return;
            lock (Gate)
            {
                if (--entry.RefCount != 0) return;
                Devices.Remove(_path);
                entry.Device.Dispose();
            }
        }
    }
}
