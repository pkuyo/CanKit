using System.Runtime.InteropServices;
using System.Text;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN;

public sealed class SocketCanBus : ICanBus<SocketCanBusRtConfigurator>, ICanApplier, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly IBusOptions _options;

    private readonly ITransceiver _transceiver;
    private int _epfd = -1;
    private CancellationTokenSource? _epollCts;
    private Task? _epollTask;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private Libc.epoll_event[] _events = new Libc.epoll_event[8];
    private int _fd;
    private EventHandler<CanReceiveData>? _frameReceived;
    private uint _ifIndex;

    private string _ifName;
    private bool _isDisposed;

    private IDisposable? _owner;

    // Cached software filter predicate to avoid rebuilding per-iteration
    private Func<ICanFrame, bool>? _softwareFilterPredicate;
    private int _subscriberCount;
    private bool _useSoftwareFilter;

    internal SocketCanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new SocketCanBusRtConfigurator();
        Options.Init((SocketCanBusOptions)options);
        _options = options;
        _transceiver = transceiver;

        // Init socket configs
        CanKitLogger.LogInformation($"SocketCAN: Initializing interface '{Options.ChannelName?? Options.ChannelIndex.ToString()}', Mode={Options.ProtocolMode}...");
        InitSocketCanConfig();

        // Create socket & bind
        _fd = CreateAndBind(Options.ChannelName, Options.ChannelIndex, Options.ProtocolMode, Options.PreferKernelTimestamp);

        // Apply initial options (filters etc.)
        _options.Apply(this, true);
        CanKitLogger.LogDebug("SocketCAN: Initial options applied.");
    }

    internal int FileDescriptor => _fd;

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    public void Apply(ICanOptions options)
    {
        if (options is not SocketCanBusOptions sc)
        {
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(SocketCanBusOptions),
                options.GetType(),
                $"channel {Options.ChannelIndex}");
        }

        // Protocol: enable FD is handled at creation time.

        // Build filter array from mask rules; respect Standard/Extended frames.
        var rules = sc.Filter.FilterRules;
        var filters = new List<Libc.can_filter>();
        if (rules.Count > 0)
        {
            foreach (var r in rules)
            {
                if (r is FilterRule.Mask m)
                {
                    uint canId, canMask;
                    if (m.FilterIdType == CanFilterIDType.Extend)
                    {
                        canId = (m.AccCode & Libc.CAN_EFF_MASK) | Libc.CAN_EFF_FLAG;
                        canMask = (m.AccMask & Libc.CAN_EFF_MASK) | Libc.CAN_EFF_FLAG;
                    }
                    else
                    {
                        canId = (m.AccCode & Libc.CAN_SFF_MASK);
                        canMask = (m.AccMask & Libc.CAN_SFF_MASK) | Libc.CAN_EFF_FLAG; // match only standard frames
                    }

                    filters.Add(new Libc.can_filter { can_id = canId, can_mask = canMask });
                }
                else
                {
                    sc.Filter.softwareFilter.Add(r);
                }
            }
        }

        if (filters.Count > 0)
        {
            var elem = Marshal.SizeOf<Libc.can_filter>();
            var total = elem * filters.Count;
            var arr = filters.ToArray();
            var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();
                if (Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, ptr, (uint)total) != 0)
                {
                    throw new CanChannelConfigurationException("setsockopt(CAN_RAW_FILTER) failed.");
                }
            }
            finally { handle.Free(); }
        }
        else
        {
            // Clear filters to receive all
            _ = Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, IntPtr.Zero, 0);
        }

        // Cache software filter predicate for event loop
        _useSoftwareFilter = (Options.EnabledSoftwareFallbackE & CanFeature.Filters) != 0
                              && Options.Filter.SoftwareFilterRules.Count > 0;
        _softwareFilterPredicate = _useSoftwareFilter
            ? FilterRule.Build(Options.Filter.SoftwareFilterRules)
            : null;
    }

    public CanOptionType ApplierStatus => _fd >= 0 ? CanOptionType.Runtime : CanOptionType.Init;

    public void Reset()
    {
        ThrowIfDisposed();
        if (LibSocketCan.can_do_restart(_ifName) != Libc.OK)
        {
            Libc.ThrowErrno("can_do_restart", $"Failed to restart interface '{_ifName}'");
        }
    }


    public uint Transmit(IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        ThrowIfDisposed();

        uint sendCount = 0;
        var startTime = Environment.TickCount;
        var pollFd = new Libc.pollfd { fd = _fd, events = Libc.POLLOUT };
        using var enumerator = frames.GetEnumerator();
        var single = new CanTransmitData[1];
        if (!enumerator.MoveNext())
            return 0;

        do
        {
            var remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;

            if (timeOut > 0 && remainingTime <= 0)
                break;

            var pr = Libc.poll(ref pollFd, 1, remainingTime);
            if (pr < 0)
            {
                Libc.ThrowErrno("poll(POLLOUT)", "Polling for writable socket failed");
            }
            if (pr == 0)
            {
                break; // timeout
            }
            single[0] = enumerator.Current;
            if (_transceiver.Transmit(this, single) == 1)
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
            var pr = Libc.poll(ref pollFd, 1, timeOut);
            if (pr == -1)
            {
                Libc.ThrowErrno("poll(POLLIN)", "Polling for readable socket failed");
            }
        }

        return _transceiver.Receive(this, count);
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        // Drain any pending frames from the socket RX queue
        var pollFd = new Libc.pollfd { fd = _fd, events = Libc.POLLIN };
        int iterations = 0;
        int maxIterations = 64;
        int readSize = Options.ProtocolMode == CanProtocolMode.CanFd
            ? Marshal.SizeOf<Libc.canfd_frame>()
            : Marshal.SizeOf<Libc.can_frame>();
        unsafe
        {
            if (Options.ProtocolMode == CanProtocolMode.CanFd)
            {
                Libc.canfd_frame* buf = stackalloc Libc.canfd_frame[1]; // 只分配一次
                while (iterations++ < maxIterations)
                {
                    var pr = Libc.poll(ref pollFd, 1, 0);
                    if (pr <= 0) break;
                    var n = Libc.read(_fd, buf, (ulong)readSize);
                    if (n <= 0)
                    {
                        //TODO:错误输出
                        break;
                    }
                }
            }
            else
            {
                Libc.can_frame* buf = stackalloc Libc.can_frame[1]; // 只分配一次
                while (iterations++ < maxIterations)
                {
                    var pr = Libc.poll(ref pollFd, 1, 0);
                    if (pr <= 0) break;
                    var n = Libc.read(_fd, buf, (ulong)readSize);
                    if (n <= 0)
                    {
                        //TODO:错误输出
                        break;
                    }
                }
            }
        }
        CanKitLogger.LogDebug("SocketCAN: RX buffer drained.");
    }

    public float BusUsage()
    {
        throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);
    }

    public CanErrorCounters ErrorCounters()
    {
        if (LibSocketCan.can_get_berr_counter(_ifName, out var counter) == Libc.OK)
        {
            return new CanErrorCounters()
            {
                ReceiveErrorCounter = counter.rxerr,
                TransmitErrorCounter = counter.txerr
            };
        }
        return Libc.ThrowErrno<CanErrorCounters>("can_get_berr_counter",
            $"Failed to get error counters for '{_ifName}'");
    }

    public IPeriodicTx TransmitPeriodic(CanTransmitData frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        return new BCMPeriodicTx(this, frame, options, Options);
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
            if (!Options.AllowErrorInfo)
            {
                throw new CanChannelConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
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

    public BusState BusState
    {
        get
        {
            ThrowIfDisposed();
            if (LibSocketCan.can_get_state(_ifName, out var i) == Libc.OK)
            {
                var state = (LibSocketCan.can_state)i;
                return state switch
                {
                    LibSocketCan.can_state.CAN_STATE_BUS_OFF => BusState.BusOff,
                    LibSocketCan.can_state.CAN_STATE_ERROR_PASSIVE => BusState.ErrPassive,
                    LibSocketCan.can_state.CAN_STATE_ERROR_WARNING => BusState.ErrWarning,
                    LibSocketCan.can_state.CAN_STATE_ERROR_ACTIVE => BusState.ErrActive,
                    _ => BusState.None
                };
            }
            else
            {
                var errno = (uint)Marshal.GetLastWin32Error();
                CanKitLogger.LogWarning($"SocketCAN: can_get_state failed for '{_ifName}', errno={errno}.");
                return BusState.Unknown;
            }
        }
    }

    private int CreateAndBind(string? ifName,int ifIndex, CanProtocolMode mode, bool preferKernelTs)
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
                Libc.ThrowErrno("fcntl(F_GETFL)", "Failed to get socket flags");
            }

            if (Libc.fcntl(fd, Libc.F_SETFL, flags | Libc.O_NONBLOCK) == -1)
            {
                Libc.ThrowErrno("fcntl(F_SETFL|O_NONBLOCK)", "Failed to set non-blocking mode");
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
                if (Libc.setsockopt(fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FD_FRAMES,
                        ref on, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    throw new CanBusCreationException("setsockopt(CAN_RAW_FD_FRAMES) failed; kernel may not support CAN FD.");
                }
            }

            // enable timestamping (prefer hardware) if requested
            if (preferKernelTs)
            {
                int tsFlags = Libc.SOF_TIMESTAMPING_RX_HARDWARE | Libc.SOF_TIMESTAMPING_RAW_HARDWARE | Libc.SOF_TIMESTAMPING_SOFTWARE;
                if (Libc.setsockopt(fd, Libc.SOL_SOCKET, Libc.SO_TIMESTAMPING,
                        ref tsFlags, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    int on = 1;
                    _ = Libc.setsockopt(fd, Libc.SOL_SOCKET, Libc.SO_TIMESTAMPNS,
                        ref on, (uint)Marshal.SizeOf<int>());
                }
            }

            // enable error frames reception (subscribe to all error classes)
            if (Options.AllowErrorInfo)
            {
                int errMask = unchecked((int)Libc.CAN_ERR_MASK);
                if (Libc.setsockopt(fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_ERR_FILTER,
                        ref errMask, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    Libc.close(fd);
                    throw new CanBusCreationException("setsockopt(CAN_RAW_ERR_FILTER) failed.");
                }
            }



            var addr = new Libc.sockaddr_can
            {
                can_family = Libc.AF_CAN,
                can_ifindex = _options.ChannelIndex,
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

    private void InitSocketCanConfig()
    {
        // bind to interface
        var ifName = Options.ChannelName;
        var ifIndex = (uint)Options.ChannelIndex;
        if (ifName != null)
        {
            ifIndex = Libc.if_nametoindex(ifName);
        }
        else
        {
            var name = new byte[16];
            if (Libc.if_indextoname(ifIndex, name) == IntPtr.Zero)
            {
                Libc.ThrowErrno("if_indextoname", "Failed to get ifname");
            }
            ifName = Encoding.ASCII.GetString(name);
        }
        if (ifIndex == 0)
        {
            throw new CanBusCreationException($"Interface '{ifName}' not found.");
        }

        _ifName = ifName;
        _ifIndex = ifIndex;

        _options.ChannelIndex = unchecked((int)ifIndex);

        if (LibSocketCan.can_get_ctrlmode(ifName, out var ctrlMode) != Libc.OK)
        {
            Libc.ThrowErrno("can_get_ctrlmode", $"Failed to get ctrlmode for '{ifName}'");
        }

        if (Options.WorkMode == ChannelWorkMode.Echo)
        {
            if ((ctrlMode.mask & LibSocketCan.CAN_CTRLMODE_LOOPBACK) == 0)
            {
                throw new CanChannelConfigurationException("Kernel driver does not support loopback (echo) mode.");
            }

            ctrlMode.flags &= ~(LibSocketCan.CAN_CTRLMODE_LISTENONLY);
            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_LOOPBACK;
        }
        else
        {
            if ((ctrlMode.mask & LibSocketCan.CAN_CTRLMODE_LISTENONLY) == 0)
            {
                throw new CanChannelConfigurationException("Kernel driver does not support listen-only mode.");
            }

            ctrlMode.flags &= ~(LibSocketCan.CAN_CTRLMODE_LOOPBACK);
            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_LISTENONLY;
        }
        if (LibSocketCan.can_get_clock(ifName, out var clock) != Libc.OK)
        {
            Libc.ThrowErrno("can_get_clock", $"Failed to get clock for '{ifName}'");
        }

        if (Options.ProtocolMode == CanProtocolMode.Can20)
        {
            var classic = Options.BitTiming.Classic!.Value;

            if (classic.clockMHz != clock.freq / 1_000_000)
            {
                //TODO:警告处理，时钟与读取到的不一致
            }

            var timing = classic.Nominal.ToCanBitTiming(clock.freq);

            if (LibSocketCan.can_set_bittiming(ifName, timing) != Libc.OK)
            {
                Libc.ThrowErrno("can_set_bitrate",
                    $"Failed to set bit timing {classic.Nominal.Bitrate!.Value} on '{ifName}'");
            }

            ctrlMode.flags &= ~LibSocketCan.CAN_CTRLMODE_FD;
        }
        else
        {

            if ((ctrlMode.mask & LibSocketCan.CAN_CTRLMODE_FD) == 0)
            {
                throw new CanFeatureNotSupportedException(CanFeature.CanFd, Options.Features);
            }
            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_FD;
            var fd = Options.BitTiming.Fd!.Value;

            if (fd.clockMHz != clock.freq / 1_000_000)
            {
                //TODO:警告处理，时钟与读取到的不一致
            }
            var aTiming = fd.Nominal.ToCanBitTiming(clock.freq);
            var dTiming = fd.Data.ToCanBitTiming(clock.freq);
            if (LibSocketCan.can_set_canfd_bittiming(ifName, aTiming, dTiming) != Libc.OK)
            {
                Libc.ThrowErrno("can_set_canfd_bittiming",
                    $"Failed to set canfd bit timing on '{ifName}'");
            }
        }

        if (Options.AllowErrorInfo)
        {
            if ((ctrlMode.mask & LibSocketCan.CAN_CTRLMODE_BERR_REPORTING) == 0)
            {
                throw new CanChannelConfigurationException("Kernel driver does not support bus-error reporting.");
            }

            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_BERR_REPORTING;
        }
        else
        {
            ctrlMode.flags &= ~LibSocketCan.CAN_CTRLMODE_BERR_REPORTING;
        }

        if (LibSocketCan.can_set_ctrlmode(ifName, ctrlMode) != Libc.OK)
        {
            Libc.ThrowErrno("can_set_ctrlmode", $"Failed to set ctrlmode on '{ifName}'");
        }

        //start device
        if (LibSocketCan.can_get_state(ifName, out var state) != Libc.OK)
        {
            Libc.ThrowErrno("can_get_state", $"Failed to get state for '{ifName}'");
        }
        if (state == (int)LibSocketCan.can_state.CAN_STATE_STOPPED)
        {
            if (LibSocketCan.can_do_start(ifName) != Libc.OK)
            {
                Libc.ThrowErrno("can_do_start", $"Failed to start interface '{ifName}'");
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }


    private void StartPollingIfNeeded()
    {
        if (_epollTask is { IsCompleted: false } || _fd < 0)
            return;

        _epfd = Libc.epoll_create1(0);
        if (_epfd < 0)
        {
            Libc.ThrowErrno("epoll_create1", "Failed to create epoll instance");
        }

        var ev = new Libc.epoll_event { events = Libc.EPOLLIN, data = (IntPtr)_fd };

        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _fd, ref ev) < 0)
        {
            Libc.ThrowErrno("epoll_ctl(EPOLL_CTL_ADD)", "failed to add fd to epoll instance");
        }

        _epollCts = new CancellationTokenSource();
        var token = _epollCts.Token;
        _epollTask = Task.Factory.StartNew(
            () => EPollLoop(token), token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        CanKitLogger.LogDebug("SocketCAN: epoll loop started.");
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
            CanKitLogger.LogDebug("SocketCAN: epoll loop stopped.");
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
                        // Build software predicate once per drain cycle if needed
                        var useSw = _useSoftwareFilter;
                        var pred = _softwareFilterPredicate;
                        foreach (var rec in _transceiver.Receive(this))
                        {
                            var frame = rec.CanFrame;
                            bool isErr = frame is CanClassicFrame { IsErrorFrame: true } ||
                                         frame is CanFdFrame { IsErrorFrame: true };

                            if (isErr)
                            {
                                uint raw = frame.RawID;
                                uint err = raw & Libc.CAN_ERR_MASK;
                                var kind = SocketCanErrors.MapToKind(err, frame.Data);
                                var sysTs = Options.PreferKernelTimestamp && rec.RecvTimestamp != 0
                                    ? new DateTime((long)rec.RecvTimestamp, DateTimeKind.Utc)
                                    : DateTime.Now;
                                var info = new DefaultCanErrorInfo(
                                    kind,
                                    sysTs,
                                    err,
                                    rec.RecvTimestamp,
                                    FrameDirection.Rx,
                                    frame);
                                _errorOccurred?.Invoke(this, info);
                            }
                            else
                            {
                                if (useSw && pred is not null && !pred(frame))
                                    continue;
                                _frameReceived?.Invoke(this, rec);
                            }
                        }
                    }
                }
            }
        }
    }
}
