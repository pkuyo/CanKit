using System;
using System.Collections.Generic;
using System.Threading;
using CanKit.Abstractions.API.Can;

namespace CanKit.Adapter.Candlelight;

internal static class CandlelightDeviceMultiplexer
{
    private static readonly object Gate = new();
    private static readonly Dictionary<uint, Entry> Devices = new();

    public static (ICanDevice device, IDisposable lease) Acquire(uint index, Func<ICanDevice> createAndOpen)
    {
        lock (Gate)
        {
            if (!Devices.TryGetValue(index, out var entry))
            {
                entry = new Entry(createAndOpen());
                Devices.Add(index, entry);
            }
            entry.RefCount++;
            return (entry.Device, new DeviceLease(index, entry));
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
        private readonly uint _index;
        private Entry? _entry;

        public DeviceLease(uint index, Entry entry) { _index = index; _entry = entry; }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry == null) return;
            lock (Gate)
            {
                if (--entry.RefCount != 0) return;
                Devices.Remove(_index);
                entry.Device.Dispose();
            }
        }
    }
}
