using System;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, VirtualBusHub> _hubs = new(StringComparer.OrdinalIgnoreCase);

    public static VirtualBusHub Get(string sessionId)
        => _hubs.GetOrAdd(sessionId ?? "default", s => new VirtualBusHub(s));

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

    internal void Attach(VirtualBus bus)
    {
        lock (_gate)
        {
            if (!_channels.Contains(bus))
                _channels.Add(bus);
        }
    }

    internal void Detach(VirtualBus bus)
    {
        lock (_gate)
        {
            _channels.Remove(bus);
        }
    }

    public void Broadcast(VirtualBus sender, CanFrame frame)
    {
        var data = new CanReceiveData(frame) { ReceiveTimestamp = TimeSpan.Zero }; // simple timestamp
        List<VirtualBus> targets;
        lock (_gate)
        {
            targets = _channels.Where(ch => !ReferenceEquals(ch, sender)).ToList();
        }

        // deliver to others
        foreach (var bus in targets)
        {
            bus.InternalDeliver(data);
        }

        // echo back if sender is in Echo mode
        if (sender.Options.WorkMode == ChannelWorkMode.Echo)
        {
            sender.InternalDeliver(data);
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

