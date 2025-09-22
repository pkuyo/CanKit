using System.Runtime.InteropServices;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.SocketCAN.Native;

namespace Pkuyo.CanKit.Net.SocketCAN;

public sealed class SocketCanBus : ICanBus<SocketCanBusRtConfigurator>, ICanApplier, IBusOwnership
{
    internal SocketCanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new SocketCanBusRtConfigurator();
        Options.Init((SocketCanBusOptions)options);
        _options = options;
        _transceiver = transceiver;

        // Create socket & bind
        _fd = CreateAndBind(Options.InterfaceName, Options.ProtocolMode, Options.PreferKernelTimestamp);

        // Apply initial options (filters etc.)
        _options.Apply(this, true);
    }

    private int CreateAndBind(string ifName, CanProtocolMode mode, bool preferKernelTs)
    {
        // create raw socket
        var fd = Libc.socket(Libc.AF_CAN, Libc.SOCK_RAW, Libc.CAN_RAW);
        if (fd < 0)
        {
            throw new CanBusCreationException("socket(AF_CAN, SOCK_RAW, CAN_RAW) failed.");
        }

        try
        {

            // set no block
            var flags = Libc.fcntl(fd, Libc.F_GETFL, 0);
            if (flags == -1)
            {
                // TODO: exception handling
            }

            if (Libc.fcntl(fd, Libc.F_SETFL, flags | Libc.O_NONBLOCK) == -1)
            {
                // TODO: exception handling
            }

            //enable echo mode
            if (_options.WorkMode == ChannelWorkMode.Echo)
            {
                int enable = 1;
                if (Libc.setsockopt(fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_RECV_OWN_MSGS, ref enable,
                        (uint)Marshal.SizeOf<int>()) != 0)
                {
                    throw new CanBusCreationException(
                        "setsockopt(CAN_RAW_RECV_OWN_MSGS) failed; kernel may not support echo mode.");
                }
            }

            // enable FD frames if needed
            if (mode == CanProtocolMode.CanFd)
            {
                int on = 1;
                if (Libc.setsockopt(fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FD_FRAMES, ref on, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    throw new CanBusCreationException("setsockopt(CAN_RAW_FD_FRAMES) failed; kernel may not support CAN FD.");
                }
            }

            // enable timestamping (prefer hardware) if requested
            if (preferKernelTs)
            {
                int tsFlags = Libc.SOF_TIMESTAMPING_RX_HARDWARE | Libc.SOF_TIMESTAMPING_RAW_HARDWARE | Libc.SOF_TIMESTAMPING_SOFTWARE;
                if (Libc.setsockopt(fd, Libc.SOL_SOCKET, Libc.SO_TIMESTAMPING, ref tsFlags, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    int on = 1;
                    _ = Libc.setsockopt(fd, Libc.SOL_SOCKET, Libc.SO_TIMESTAMPNS, ref on, (uint)Marshal.SizeOf<int>());
                }
            }

            // enable error frames reception (subscribe to all error classes)
            if(Options.AllowErrorInfo)
            {
                int errMask = unchecked((int)Libc.CAN_ERR_MASK);
                if (Libc.setsockopt(fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_ERR_FILTER, ref errMask, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    Libc.close(fd);
                    throw new CanBusCreationException("setsockopt(CAN_RAW_ERR_FILTER) failed.");
                }
            }

            // bind to interface
            uint ifIndex = Libc.if_nametoindex(ifName);
            if (ifIndex == 0)
            {
                Libc.close(fd);
                throw new CanBusCreationException($"Interface '{ifName}' not found.");
            }

            var addr = new Libc.sockaddr_can
            {
                can_family = Libc.AF_CAN,
                can_ifindex = unchecked((int)ifIndex),
            };

            if (Libc.bind(fd, ref addr, Marshal.SizeOf<Libc.sockaddr_can>()) != 0)
            {
                Libc.close(fd);
                throw new CanBusCreationException($"bind({ifName}) failed.");
            }

            return fd;
        }
        catch
        {
            try { Libc.close(fd); } catch { /* ignored */ }
            throw;
        }
    }



    public void Reset()
    {
        ThrowIfDisposed();
        //TODO:
    }



    public uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        ThrowIfDisposed();

        uint sendCount = 0;
        var startTime = Environment.TickCount;
        var pollFd = new Libc.pollfd { fd = _fd, events = Libc.POLLOUT };
        using var enumerator = frames.GetEnumerator();
        if (!enumerator.MoveNext())
            return 0;

        do
        {
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;

            if (timeOut > 0 && remainingTime <= 0)
                break;

            if (Libc.poll(ref pollFd, 1, remainingTime) <= 0)
            {
                // TODO: exception handling
                break;
            }

            if (_transceiver.Transmit(this, [enumerator.Current!]) == 1)
            {
                sendCount++;
                if (!enumerator.MoveNext())
                {
                    break;
                }
            }
        } while (true);

        return sendCount;
    }

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();

        if (timeOut > 0)
        {
            var pollFd = new Libc.pollfd { fd = _fd, events = Libc.POLLIN };
            if (Libc.poll(ref pollFd, 1, timeOut) == -1)
            {
                // TODO: exception handling
            }
        }

        return _transceiver.Receive(this, count);
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();

        // TODO: exception handling
        throw new NotImplementedException();
    }
    public float BusUsage()
    {
        throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    }

    public CanErrorCounters ErrorCounters()
    {
        throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Features);
    }

    public bool ReadErrorInfo(out ICanErrorInfo? errorInfo)
    {
        // SocketCAN via raw socket does not expose detailed error info here
        errorInfo = null;
        return false;
    }


    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {
            StopPolling();
            if (_fd >= 0)
                Libc.close(_fd);
        }
        finally
        {
            _fd = -1;
            _isDisposed = true;
            try { _owner?.Dispose(); } catch { }
            _owner = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }

    public void Apply(ICanOptions options)
    {
        if (options is not SocketCanBusOptions sc)
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(SocketCanBusOptions),
                options?.GetType() ?? typeof(IBusOptions),
                $"channel {Options.ChannelIndex}");

        // Protocol: enable FD is handled at creation time.

        // Build filter array from mask rules; respect Standard/Extended frames.
        var rules = sc.Filter.FilterRules;
        if (rules.Count > 0)
        {
            var filters = new List<Libc.can_filter>();
            foreach (var r in rules)
            {
                if (r is not FilterRule.Mask m)
                    throw new CanFilterConfigurationException("SocketCAN only supports mask filters.");

                uint can_id, can_mask;
                if (m.FilterIdType == CanFilterIDType.Extend)
                {
                    can_id = (m.AccCode & Libc.CAN_EFF_MASK) | Libc.CAN_EFF_FLAG;
                    can_mask = (m.AccMask & Libc.CAN_EFF_MASK) | Libc.CAN_EFF_FLAG;
                }
                else
                {
                    can_id = (m.AccCode & Libc.CAN_SFF_MASK);
                    can_mask = (m.AccMask & Libc.CAN_SFF_MASK) | Libc.CAN_EFF_FLAG; // match only standard frames
                }

                filters.Add(new Libc.can_filter { can_id = can_id, can_mask = can_mask });
            }

            var elem = Marshal.SizeOf<Libc.can_filter>();
            var total = elem * filters.Count;
            var arr = filters.ToArray();
            var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                if (Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, ptr, (uint)total) != 0)
                    throw new CanChannelConfigurationException("setsockopt(CAN_RAW_FILTER) failed.");
            }
            finally { handle.Free(); }
        }
        else
        {
            // Clear filters to receive all
            _ = Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, IntPtr.Zero, 0);
        }
    }

    public CanOptionType ApplierStatus => _fd >= 0 ? CanOptionType.Runtime : CanOptionType.Init;


    private void StartPollingIfNeeded()
    {
        if (_epollTask is { IsCompleted: false } || _fd < 0)
            return;

        _epfd = Libc.epoll_create1(0);
        if (_epfd < 0)
            Libc.ThrowErrno("epoll_create1");
        var ev = new Libc.epoll_event { events = Libc.EPOLLIN, data = (IntPtr)_fd };

        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _fd, ref ev) < 0)
            Libc.ThrowErrno("epoll_ctl ADD sock");

        _epollCts = new CancellationTokenSource();
        var token = _epollCts.Token;
        _epollTask = Task.Factory.StartNew(
            () => EPollLoop(token), token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void StopPolling()
    {
        try
        {
            _epollCts?.Cancel();
            _epollTask?.Wait(200);
        }
        catch
        {
            // ignored
        }
        finally
        {
            if (_epfd >= 0)
                Libc.close(_epfd);

            _epollTask = null;
            _epollCts?.Dispose();
            _epollCts = null;
        }
    }

    private void EPollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Volatile.Read(ref _subscriberCount) <= 0) break;

            int n = Libc.epoll_wait(_epfd, _events, _events.Length, 500);
            if (n < 0) { continue; }

            for (int i = 0; i < n; i++)
            {
                if ((_events[i].events & Libc.EPOLLIN) != 0)
                {
                    while (true)
                    {
                        // drain available data without blocking
                        int bytes = 0;
                        if (Libc.ioctl(_fd, Libc.FIONREAD, ref bytes) != 0 || bytes <= 0)
                            break;

                        // receive one frame via transceiver
                        foreach (var rec in _transceiver.Receive(this, 1))
                        {
                            var frame = rec.CanFrame;
                            bool isErr = frame is CanClassicFrame { IsErrorFrame: true } ||
                                         frame is CanFdFrame { IsErrorFrame: true };

                            if (isErr)
                            {
                                uint raw = frame.RawID;
                                uint err = raw & Libc.CAN_ERR_MASK;
                                var kind = SocketCanErrors.MapToKind(err, frame.Data);
                                var sysTs = Options.PreferKernelTimestamp && rec.recvTimestamp != 0
                                    ? new DateTime((long)rec.recvTimestamp, DateTimeKind.Utc)
                                    : DateTime.Now;
                                var info = new DefaultCanErrorInfo(
                                    kind,
                                    sysTs,
                                    err,
                                    rec.recvTimestamp,
                                    FrameDirection.Rx,
                                    frame);
                                _errorOccurred?.Invoke(this, info);
                            }
                            else
                            {
                                _frameReceived?.Invoke(this, rec);
                            }
                        }
                    }
                }
            }
        }
    }

    public SocketCanBusRtConfigurator Options { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public event EventHandler<CanReceiveData> FrameReceived
    {
        add
        {
            lock (_evtGate)
            {
                _frameReceived += value;
                _subscriberCount++;
                StartPollingIfNeeded();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameReceived -= value;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                if (_subscriberCount == 0) StopPolling();
            }
        }
    }

    public event EventHandler<ICanErrorInfo> ErrorOccurred
    {
        add
        {
            //TODO:在未启用时抛出异常
            lock (_evtGate)
            {
                _errorOccurred += value;
                _subscriberCount++;
                StartPollingIfNeeded();
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _errorOccurred -= value;
                _subscriberCount = Math.Max(0, _subscriberCount - 1);
                if (_subscriberCount == 0) StopPolling();
            }
        }
    }

    internal int FileDescriptor => _fd;

    private readonly ITransceiver _transceiver;
    private bool _isDisposed;
    private int _fd;

    private readonly IBusOptions _options;


    private readonly object _evtGate = new();
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private int _subscriberCount;
    private CancellationTokenSource? _epollCts;
    private Task? _epollTask;
    private int _epfd = -1;
    private Libc.epoll_event[] _events = new Libc.epoll_event[8];

    private IDisposable? _owner;

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }
}
