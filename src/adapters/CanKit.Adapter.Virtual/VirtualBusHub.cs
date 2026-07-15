using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Virtual;

/// <summary>
/// Hub that connects all VirtualBus instances sharing the same SessionId.
/// Provides broadcast and error injection.
/// </summary>
public sealed class VirtualBusHub
{
    // Guards both the static registry (creation/removal of hub instances) and, in nested
    // fashion, the per-hub _gate below. Only entered on join/leave (session setup/teardown),
    // never on the Broadcast/InjectError hot path.
    private static readonly object _hubsGate = new();
    private static readonly Dictionary<string, VirtualBusHub> _hubs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets (creating if necessary) the hub for the given session and atomically attaches
    /// <paramref name="bus"/> to it, so a concurrent detach of the last member of the same
    /// session cannot remove the hub between lookup and attach.
    /// 获取（必要时创建）指定会话的 Hub，并原子化地将 <paramref name="bus"/> 挂载到该 Hub 上，
    /// 避免并发场景下，同一会话最后一个成员的 Detach 恰好在查找与挂载之间将该 Hub 移除。
    /// </summary>
    internal static VirtualBusHub Join(string sessionId, VirtualBus bus)
    {
        var key = sessionId ?? "default";
        lock (_hubsGate)
        {
            if (!_hubs.TryGetValue(key, out var hub))
            {
                hub = new VirtualBusHub(key);
                _hubs[key] = hub;
            }
            hub.Attach(bus);
            return hub;
        }
    }

    private readonly string _sessionId;
    private readonly object _gate = new();
    private readonly List<VirtualBus> _channels = new();

    private volatile BusState _busState = BusState.None;
    private int _tec;
    private int _rec;

    private VirtualBusHub(string sessionId)
    {
        _sessionId = sessionId;
    }

    public string SessionId => _sessionId;

    private void Attach(VirtualBus bus)
    {
        lock (_gate)
        {
            if (!_channels.Contains(bus))
                _channels.Add(bus);
        }
    }

    internal void Detach(VirtualBus bus)
    {
        lock (_hubsGate)
        {
            lock (_gate)
            {
                _channels.Remove(bus);
                // Remove the hub from the registry once its last member leaves, so sessions
                // don't accumulate forever (previously _hubs grew without bound: Review §2.4).
                // Held under _hubsGate together with Join() so a concurrent Join() for the
                // same session either completes before this removal (hub survives) or starts
                // a fresh hub afterwards (no lost attach).
                if (_channels.Count == 0 && _hubs.TryGetValue(_sessionId, out var current) && ReferenceEquals(current, this))
                {
                    _hubs.Remove(_sessionId);
                }
            }
        }
    }

    public void Broadcast(VirtualBus sender, CanFrame frame)
    {
        List<VirtualBus> targets;
        lock (_gate)
        {
            targets = _channels.Where(ch => !ReferenceEquals(ch, sender)).ToList();
        }

        // Frame ownership contract (docs/architecture/arc42-CanKit.md §8.1): the sender keeps
        // owning `frame` (TX-lease) and each recipient's RX-lease must be an independently
        // disposable copy, so one consumer disposing/dropping its frame can never invalidate
        // another consumer's (or the sender's) memory.
        foreach (var bus in targets)
        {
            var copy = frame.Duplicate(bus.Options.BufferAllocator);
            var data = new CanReceiveData(copy) { ReceiveTimestamp = TimeSpan.Zero };
            bus.InternalDeliver(data);
        }

        // echo back if sender is in Echo mode
        if (sender.Options.WorkMode == ChannelWorkMode.Echo)
        {
            var copy = frame.Duplicate(sender.Options.BufferAllocator);
            var echoData = new CanReceiveData(copy) { ReceiveTimestamp = TimeSpan.Zero, IsEcho = true };
            sender.InternalDeliver(echoData);
        }
    }

    public void InjectError(ICanErrorInfo error)
    {
        List<VirtualBus> targets;
        lock (_gate)
        {
            targets = _channels.ToList();
        }
        foreach (var bus in targets)
        {
            bus.InternalInjectError(error);
        }
    }

    public void SetBusState(BusState state)
    {
        _busState = state;
    }

    public BusState GetBusState() => _busState;

    public void SetErrorCounters(int tec, int rec)
    {
        Interlocked.Exchange(ref _tec, tec);
        Interlocked.Exchange(ref _rec, rec);
    }

    public CanErrorCounters GetErrorCounters()
        => new CanErrorCounters { TransmitErrorCounter = _tec, ReceiveErrorCounter = _rec };
}

