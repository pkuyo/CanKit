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
    // Guards the static registry (creation/removal of hubs) and, nested, the per-hub _gate.
    // Only taken on join/leave, never on the Broadcast/InjectError path.
    private static readonly object _hubsGate = new();
    private static readonly Dictionary<string, VirtualBusHub> _hubs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets (creating if necessary) the hub for the session. A hub returned here is pinned: it stays
    /// registered after its last bus leaves, so the handle keeps working when the session is opened again.
    /// Hubs that were only joined by buses are removed when the last bus leaves.
    /// </summary>
    public static VirtualBusHub Get(string sessionId)
    {
        lock (_hubsGate)
        {
            var hub = GetOrCreate(sessionId ?? "default");
            hub._pinned = true;
            return hub;
        }
    }

    /// <summary>
    /// Gets the hub for the session and attaches <paramref name="bus"/> in one step, so the last member of the
    /// session cannot leave and remove the hub between lookup and attach.
    /// </summary>
    internal static VirtualBusHub Join(string sessionId, VirtualBus bus)
    {
        lock (_hubsGate)
        {
            var hub = GetOrCreate(sessionId ?? "default");
            hub.Attach(bus);
            return hub;
        }
    }

    private static VirtualBusHub GetOrCreate(string key)
    {
        if (!_hubs.TryGetValue(key, out var hub))
        {
            hub = new VirtualBusHub(key);
            _hubs[key] = hub;
        }
        return hub;
    }

    private bool _pinned; // guarded by _hubsGate
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
                // Drop the hub once its last member leaves so sessions do not accumulate.
                if (_channels.Count == 0 && !_pinned && _hubs.TryGetValue(_sessionId, out var current) && ReferenceEquals(current, this))
                {
                    _hubs.Remove(_sessionId);
                }
            }
        }
    }

    /// <summary>
    /// Delivers <paramref name="frame"/> to every other member of the session. The sender keeps ownership of
    /// <paramref name="frame"/>; each recipient (and the echo, in Echo mode) gets its own copy, so one consumer
    /// disposing its frame never invalidates another consumer's or the sender's memory.
    /// </summary>
    public void Broadcast(VirtualBus sender, CanFrame frame)
    {
        List<VirtualBus> targets;
        lock (_gate)
        {
            targets = _channels.Where(ch => !ReferenceEquals(ch, sender)).ToList();
        }

        foreach (var bus in targets)
        {
            var data = new CanReceiveData(frame.Copy(bus.Options.BufferAllocator)) { ReceiveTimestamp = TimeSpan.Zero };
            bus.InternalDeliver(data);
        }

        // echo back if sender is in Echo mode
        if (sender.Options.WorkMode == ChannelWorkMode.Echo)
        {
            var echo = new CanReceiveData(frame.Copy(sender.Options.BufferAllocator))
            {
                ReceiveTimestamp = TimeSpan.Zero,
                IsEcho = true
            };
            sender.InternalDeliver(echo);
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

