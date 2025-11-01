using System;
using System.Collections.Concurrent;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ZLG;

/// <summary>
/// Manages shared ZLG device across buses (使用引用计数在多总线间共享 ZLG 设备)。
/// </summary>
internal static class ZlgDeviceMultiplexer
{
    private static readonly ConcurrentDictionary<string, Entry> _map = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(DeviceType dt, uint index) => $"{dt.Id}|{index}";

    /// <summary>
    /// Acquire device lease by type/index (获取指定设备类型与索引的设备租约)。不存在时将创建并打开设备。
    /// 返回设备和引用计数
    /// </summary>
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
            try
            {
                dev.Dispose(); //已经在另一线程创建相同的Device，销毁现有
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

        public Entry(ICanDevice device)
        {
            Device = device;
            RefCount = 1;
        }

        public ICanDevice Device { get; }
    }

    //引用计数
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
