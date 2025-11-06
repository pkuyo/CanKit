using System.Collections.Concurrent;
using CanKit.Abstractions.API.Transport;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Transport;

public class PcanIsoTpBusMultiplexer
{
    private static readonly ConcurrentDictionary<PcanChannel, Entry> _map = new();

    public static (IIsoTpChannel channel, IDisposable lease) Acquire(PcanChannel handle, Func<IIsoTpScheduler> createBus,
        Func<IIsoTpScheduler, IIsoTpChannel> createChannel)
    {
        while (true)
        {
            if (_map.TryGetValue(handle, out var existing))
            {
                Interlocked.Increment(ref existing.RefCount);
                var channel = createChannel(existing.Scheduler);
                return (channel, new BusLease(handle, existing));
            }

            var bus = createBus();
            var entry = new Entry(bus);
            if (_map.TryAdd(handle, entry))
            {
                var channel = createChannel(bus);
                return (channel, new BusLease(handle, entry));
            }
            try
            {
                bus.Dispose(); //已经在另一线程创建相同的Device，销毁现有
            }
            catch
            {
                // 忽略
            }
        }
    }
    private sealed class Entry
    {
        public int RefCount;

        public Entry(IIsoTpScheduler scheduler)
        {
            Scheduler = scheduler;
            RefCount = 1;
        }

        public IIsoTpScheduler Scheduler { get; }
    }

    private sealed class BusLease : IDisposable
    {
        private readonly PcanChannel _handle;
        private Entry? _entry;

        public BusLease(PcanChannel handle, Entry entry)
        {
            _handle = handle; _entry = entry;
        }

        public void Dispose()
        {
            var e = Interlocked.Exchange(ref _entry, null);
            if (e == null) return;
            var c = Interlocked.Decrement(ref e.RefCount);
            if (c > 0) return;
            if (_map.TryRemove(_handle, out _))
            {
                try { e.Scheduler.Dispose(); } catch { }
            }
        }
    }
}
