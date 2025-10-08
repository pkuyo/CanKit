using System.Runtime.InteropServices;
using System.Text;
using CanKit.Adapter.SocketCAN.Diagnostics;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using CanKit.Core.Utils;

namespace CanKit.Adapter.SocketCAN;

public sealed class SocketCanBus : ICanBus<SocketCanBusRtConfigurator>, IBusOwnership
{
    private readonly object _evtGate = new();

    private readonly IBusOptions _options;

    private readonly ITransceiver _transceiver;
    private int _epfd = -1;
    private int _cancelFd = -1;
    private CancellationTokenSource? _epollCts;
    private Task? _epollTask;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private Libc.epoll_event[] _events = new Libc.epoll_event[8];
    private int _fd;
    private string _ifName;
    private EventHandler<CanReceiveData>? _frameReceived;

    private bool _isDisposed;

    private IDisposable? _owner;

    // Cached software filter predicate to avoid rebuilding per-iteration
    private Func<ICanFrame, bool>? _softwareFilterPredicate;
    private int _subscriberCount;
    private bool _useSoftwareFilter;
    private readonly AsyncFramePipe _asyncRx;
    private int _asyncConsumerCount;
    private CancellationTokenSource? _stopDelayCts;

    internal SocketCanBus(IBusOptions options, ITransceiver transceiver)
    {
        Options = new SocketCanBusRtConfigurator();
        Options.Init((SocketCanBusOptions)options);
        _options = options;
        _transceiver = transceiver;
        _ifName = string.Empty;
        // init async pipe with configured capacity
        var cap = Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : (int?)null;
        _asyncRx = new AsyncFramePipe(cap);

        // Apply device configs
        CanKitLogger.LogInformation($"SocketCAN: Initializing interface '{Options.ChannelName?? Options.ChannelIndex.ToString()}', Mode={Options.ProtocolMode}...");


        ApplyDeviceConfig();


        // Create socket & bind
        _fd = CreateAndBind(Options.ChannelName, Options.ProtocolMode, Options.PreferKernelTimestamp);

        // Apply socket options
        ApplySocketConfig(options);
        CanKitLogger.LogDebug("SocketCAN: Initial options applied.");
    }

    internal int FileDescriptor => _fd;

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }

    public void ApplySocketConfig(ICanOptions options)
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
                else if ((Options.EnabledSoftwareFallback & CanFeature.Filters) != Libc.OK)
                {
                    sc.Filter.softwareFilter.Add(r);
                }
            }
        }
        var tv = Libc.ToTimeval(TimeSpan.FromMilliseconds(Options.ReadTImeOutMs));

        unsafe
        {
            if (Libc.setsockopt(_fd, Libc.SOL_SOCKET, Libc.SO_SNDTIMEO, &tv,
                    (uint)Marshal.SizeOf<Libc.timeval>()) != Libc.OK)
            {
                throw new CanBusConfigurationException("setsockopt(SO_SNDTIMEO) failed.");
            }
        }


        if (filters.Count > 0)
        {
            var elem = Marshal.SizeOf<Libc.can_filter>();
            var total = elem * filters.Count;
            var arr = filters.ToArray();
            unsafe
            {
                fixed (Libc.can_filter* pArr = arr)
                {
                    if (Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, pArr, (uint)total) != Libc.OK)
                    {
                        throw new CanBusConfigurationException("setsockopt(CAN_RAW_FILTER) failed.");
                    }
                }
            }
        }
        else
        {
            // set filter to receive all
            var all = new Libc.can_filter { can_id = 0, can_mask = 0 };
            var sz = Marshal.SizeOf<Libc.can_filter>();
            unsafe
            {
                Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, &all, (uint)sz);
            }
        }

        if (Options.ReceiveBufferCapacity != null)
        {
            var cap = (int)Options.ReceiveBufferCapacity.Value;
            Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.SO_RCVBUF, ref cap, (uint)Marshal.SizeOf<int>());
        }

        // Cache software filter predicate for event loop
        _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.Filters) != 0
                              && Options.Filter.SoftwareFilterRules.Count > 0;
        _softwareFilterPredicate = _useSoftwareFilter
            ? FilterRule.Build(Options.Filter.SoftwareFilterRules)
            : null;
    }

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
        int sendCount = 0;
        var startTime = Environment.TickCount;
        var pollFd = new Libc.pollfd { fd = _fd, events = Libc.POLLOUT };
        var needSend = frames.ToArray();
        var wrote = _transceiver.Transmit(this, needSend.Skip(sendCount));
        sendCount = (int)wrote;
        var remainingTime = timeOut;
        while ((timeOut < 0 || remainingTime > 0) && sendCount < needSend.Length)
        {
            remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;


            var pr = Libc.poll(ref pollFd, 1, remainingTime);
            if (pr < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EINTR) continue;
                Libc.ThrowErrno("poll(POLLOUT)", "Polling for writable socket failed");
            }
            if (pr == 0)
            {
                break;
            }

            if ((pollFd.revents & (Libc.POLLERR | Libc.POLLHUP | Libc.POLLNVAL)) != 0)
            {
                Libc.ThrowErrno("poll(POLLOUT)", "socket error");
            }

            wrote = _transceiver.Transmit(this,
                new ArraySegment<CanTransmitData>(needSend, sendCount, needSend.Length - sendCount));
            sendCount += (int)wrote;

        }

        return (uint)sendCount;

    }

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        if (timeOut == 0)
            return _transceiver.Receive(this, count);
        var recList = new List<CanReceiveData>((int)count);
        var startTime = Environment.TickCount;
        var pollFd = new Libc.pollfd { fd = _fd, events = Libc.POLLIN };
        var remainingTime = timeOut;

        while ((timeOut < 0 || remainingTime > 0) && recList.Count < count)
        {
            remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (Environment.TickCount - startTime))
                : timeOut;


            var pr = Libc.poll(ref pollFd, 1, remainingTime);
            if (pr < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EINTR) continue;
                Libc.ThrowErrno("poll(POLLIN)", "Polling for readable socket failed");
            }
            if (pr == 0)
            {
                break;
            }
            if ((pollFd.revents & (Libc.POLLERR | Libc.POLLHUP | Libc.POLLNVAL)) != 0)
            {
                Libc.ThrowErrno("poll(POLLIN)", "socket error");
            }

            var batch = _transceiver.Receive(this, (uint)(count - recList.Count));
            foreach (var item in batch)
            {
                recList.Add(item);
                if (recList.Count == count)
                    break;
            }
        }
        return recList;
    }

    public Task<uint> TransmitAsync(IEnumerable<CanTransmitData> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try { return Transmit(frames, timeOut); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { HandleBackgroundException(ex); throw; }
        }, cancellationToken);

    public Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(uint count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        StartReceiveLoopIfNeeded();
        return _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ContinueWith(t =>
            {
                var remAsync = Interlocked.Decrement(ref _asyncConsumerCount);
                var rem = Interlocked.Decrement(ref _subscriberCount);
                if (rem == 0 && remAsync == 0) RequestStopReceiveLoop();
                return t.Result;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

#if NET8_0_OR_GREATER
    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _subscriberCount);
        Interlocked.Increment(ref _asyncConsumerCount);
        StartReceiveLoopIfNeeded();
        try
        {
            await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            var remAsync = Interlocked.Decrement(ref _asyncConsumerCount);
            var rem = Interlocked.Decrement(ref _subscriberCount);
            if (rem == 0 && remAsync == 0) RequestStopReceiveLoop();
        }
    }
#endif

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
                        CanKitLogger.LogWarning(
                            $"SocketCAN: read(FD) returned {n} while draining RX buffer. errno={CanKit.Adapter.SocketCAN.Native.Libc.Errno()}");
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
                        CanKitLogger.LogWarning(
                            $"SocketCAN: read(FD) returned {n} while draining RX buffer. errno={CanKit.Adapter.SocketCAN.Native.Libc.Errno()}");
                        break;
                    }
                }
            }
        }
        CanKitLogger.LogDebug("SocketCAN: RX buffer drained.");
    }

    public float BusUsage() => throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Features);

    public CanErrorCounters ErrorCounters()
    {
        CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.ErrorCounters);
        if (LibSocketCan.can_get_berr_counter(_ifName, out var counter) == Libc.OK)
        {
            return new CanErrorCounters()
            {
                ReceiveErrorCounter = counter.rxerr,
                TransmitErrorCounter = counter.txerr
            };
        }

        Libc.ThrowErrno("can_get_berr_counter",
            $"Failed to get error counters for '{_ifName}'");

        return default;
    }

    public IPeriodicTx TransmitPeriodic(CanTransmitData frame, PeriodicTxOptions options)
    {
        ThrowIfDisposed();
        return new BCMPeriodicTx(this, frame, options, Options);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {
            StopReceiveLoop();
            if (_fd >= 0)
                Libc.close(_fd);
        }
        finally
        {
            _fd = -1;
            _isDisposed = true;
            try { _owner?.Dispose(); } catch { /*ignore for dispose*/ }
            _owner = null;
        }
    }

    public SocketCanBusRtConfigurator Options { get; }

    IBusRTOptionsConfigurator ICanBus.Options => Options;

    public event EventHandler<CanReceiveData> FrameReceived
    {
        add
        {
            bool needStart = false;
            lock (_evtGate)
            {
                var before = _frameReceived;
                _frameReceived += value;
                if (!ReferenceEquals(before, _frameReceived))
                {
                    Interlocked.Increment(ref _subscriberCount);
                    needStart = (before == null) && Volatile.Read(ref _asyncConsumerCount) == 0;
                }
            }
            if (needStart) StartReceiveLoopIfNeeded();
        }
        remove
        {
            bool needStop = false;
            lock (_evtGate)
            {
                var before = _frameReceived;
                _frameReceived -= value;
                if (!ReferenceEquals(before, _frameReceived))
                {
                    var now = Interlocked.Decrement(ref _subscriberCount);
                    if (now == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                        needStop = true;
                }
            }
            if (needStop) RequestStopReceiveLoop();
        }
    }

    public event EventHandler<ICanErrorInfo> ErrorFrameReceived
    {
        add
        {
            if (!Options.AllowErrorInfo)
            {
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
            bool needStart = false;
            lock (_evtGate)
            {
                var before = _errorOccurred;
                _errorOccurred += value;
                if (!ReferenceEquals(before, _errorOccurred))
                {
                    Interlocked.Increment(ref _subscriberCount);
                    if ((before == null) && Volatile.Read(ref _asyncConsumerCount) == 0)
                        needStart = true;
                }
            }
            if (needStart) StartReceiveLoopIfNeeded();
        }
        remove
        {
            if (!Options.AllowErrorInfo)
            {
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
            bool needStop = false;
            lock (_evtGate)
            {
                var before = _errorOccurred;
                _errorOccurred -= value;
                if (!ReferenceEquals(before, _errorOccurred))
                {
                    var now = Interlocked.Decrement(ref _subscriberCount);
                    if (now == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                        needStop = true;
                }
            }
            if (needStop) RequestStopReceiveLoop();
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
                var errno = Libc.Errno();
                CanKitLogger.LogWarning($"SocketCAN: can_get_state failed for '{_ifName}', errno={errno}.");
                return BusState.Unknown;
            }
        }
    }

    private int CreateAndBind(string? ifName, CanProtocolMode mode, bool preferKernelTs)
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

    private void ApplyDeviceConfig()
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

        _options.ChannelIndex = unchecked((int)ifIndex);

        if (!Options.UseNetLink)
        {
            return;
        }

        if (LibSocketCan.can_get_ctrlmode(ifName, out var ctrlMode) != Libc.OK)
        {
            var re = Libc.Errno();
            if (re == Libc.EOPNOTSUPP)
            {
                CanKitLogger.LogInformation($"SocketCanBus: {ifName} not support ctrlmode. Ignored socket can config.");
                return;
            }
            Libc.ThrowErrno("can_get_ctrlmode", $"Failed to get ctrlmode for '{ifName}'", re);
        }

        UpdateDynamicFeatures(ctrlMode.mask);

        if (Options.WorkMode == ChannelWorkMode.Echo)
        {
            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.Echo);
            ctrlMode.flags &= ~(LibSocketCan.CAN_CTRLMODE_LISTENONLY);
            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_LOOPBACK;
        }
        else if (Options.WorkMode == ChannelWorkMode.ListenOnly)
        {
            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.ListenOnly);
            ctrlMode.flags &= ~(LibSocketCan.CAN_CTRLMODE_LOOPBACK);
            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_LISTENONLY;
        }
        else
        {
            ctrlMode.flags &= ~(LibSocketCan.CAN_CTRLMODE_LOOPBACK);
            ctrlMode.flags &= ~(LibSocketCan.CAN_CTRLMODE_LISTENONLY);
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
                CanKit.Core.Diagnostics.CanKitLogger.LogWarning(
                    $"SocketCanBus: timing clock ({classic.clockMHz} MHz) differs from device clock ({clock.freq / 1_000_000} MHz); using device clock.");
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
            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.CanFd);

            ctrlMode.flags |= LibSocketCan.CAN_CTRLMODE_FD;
            var fd = Options.BitTiming.Fd!.Value;
            if (fd.clockMHz != clock.freq / 1_000_000)
            {
                CanKitLogger.LogWarning(
                    $"SocketCanBus: timing clock ({fd.clockMHz} MHz) differs from device clock ({clock.freq / 1_000_000} MHz); using device clock.");
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
            CanKitErr.ThrowIfNotSupport(Options.Features, CanFeature.ErrorFrame);
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

    private void UpdateDynamicFeatures(uint mask)
    {
        var features = CanFeature.CanClassic | CanFeature.Filters | CanFeature.ErrorCounters;
        if ((mask & LibSocketCan.CAN_CTRLMODE_LOOPBACK) != 0)
            features |= CanFeature.Echo;
        if ((mask & LibSocketCan.CAN_CTRLMODE_LISTENONLY) != 0)
            features |= CanFeature.ListenOnly;
        if ((mask & LibSocketCan.CAN_CTRLMODE_FD) != 0)
            features |= CanFeature.CanFd;
        if ((mask & LibSocketCan.CAN_CTRLMODE_BERR_REPORTING) != 0)
            features |= CanFeature.ErrorFrame;
        if (LibSocketCan.can_get_berr_counter(_ifName, out _) == Libc.OK)
            features |= CanFeature.ErrorCounters;

        Options.UpdateDynamicFeatures(features);

    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }


    private void StartReceiveLoopIfNeeded()
    {
        if (_epollTask is { IsCompleted: false } || _fd < 0)
            return;

        _epfd = Libc.epoll_create1(Libc.EPOLL_CLOEXEC);
        if (_epfd < 0)
        {
            Libc.ThrowErrno("epoll_create1", "Failed to create epoll instance");
        }

        // create cancel eventfd for immediate epoll wake on stop
        _cancelFd = Libc.eventfd(0, Libc.EFD_NONBLOCK | Libc.EFD_CLOEXEC);
        if (_cancelFd < 0)
        {
            Libc.ThrowErrno("eventfd", "Failed to create eventfd for cancellation");
        }
#if NET8_0_OR_GREATER
        var ev = new Libc.epoll_event { events = Libc.EPOLLIN, data = (IntPtr)_fd };
#else
        var ev = new Libc.epoll_event { events = Libc.EPOLLIN | Libc.EPOLLERR, data = (IntPtr)_fd };
#endif
        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _fd, ref ev) < 0)
        {
            Libc.ThrowErrno("epoll_ctl(EPOLL_CTL_ADD)", "failed to add fd to epoll instance");
        }

        // add cancel fd to epoll
        var evCancel = new Libc.epoll_event { events = Libc.EPOLLIN, data = (IntPtr)_cancelFd };
        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _cancelFd, ref evCancel) < 0)
        {
            Libc.ThrowErrno("epoll_ctl(EPOLL_CTL_ADD)", "failed to add cancelfd to epoll instance");
        }

        _epollCts = new CancellationTokenSource();
        var token = _epollCts.Token;
        _epollTask = Task.Factory.StartNew(
            () => EPollLoop(token), token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        CanKitLogger.LogDebug("SocketCAN: epoll loop started.");
    }

    private void StopReceiveLoop()
    {
        try
        {
            _epollCts?.Cancel();
            // wake epoll_wait immediately via eventfd
            try
            {
                if (_cancelFd >= 0)
                {
                    unsafe
                    {
                        ulong one = 1UL;
                        _ = Libc.write(_cancelFd, &one, (ulong)sizeof(ulong));
                    }
                }
            }
            catch { /* ignore wake errors */ }
            _epollTask?.Wait(200);
        }
        catch { /*ignored*/ }
        finally
        {
            if (_epfd >= 0)
                Libc.close(_epfd);
            if (_cancelFd >= 0)
                Libc.close(_cancelFd);

            _epollTask = null;
            _epollCts?.Dispose();
            _epollCts = null;
            _epfd = -1;
            _cancelFd = -1;
            CanKitLogger.LogDebug("SocketCAN: epoll loop stopped.");
        }
    }

    private void RequestStopReceiveLoop()
    {
        var delay = Options.ReceiveLoopStopDelayMs;
        if (delay <= 0)
        {
            StopReceiveLoop();
            return;
        }
        _stopDelayCts?.Cancel();
        var cts = new CancellationTokenSource();
        _stopDelayCts = cts;
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                if (Volatile.Read(ref _subscriberCount) == 0 && Volatile.Read(ref _asyncConsumerCount) == 0)
                {
                    StopReceiveLoop();
                }
            }
            catch { /*Ignored*/ }
        }, cts.Token);
    }

    private void EPollLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (Volatile.Read(ref _subscriberCount) <= 0) break;

                int n = Libc.epoll_wait(_epfd, _events, _events.Length, -1);
                if (n < 0)
                {
                    var errno = Libc.Errno();
                    if (errno == Libc.EINTR)
                        continue;
                    throw new SocketCanNativeException("epoll_wait(FD)", "epoll_wait occured an error", (uint)errno);
                }

                for (int i = 0; i < n; i++)
                {
                    var fd = (int)_events[i].data;
                    if (fd == _cancelFd)
                    {
                        try
                        {
                            unsafe
                            {
                                ulong tmp;
                                _ = Libc.read(_cancelFd, &tmp, sizeof(ulong));
                            }
                        }
                        catch { /* ignore */ }
                        continue;
                    }

                    if ((_events[i].events & Libc.EPOLLIN) != 0 && fd == _fd)
                    {
                        DrainReceive();
                    }
                    else if ((_events[i].events & Libc.EPOLLERR) != 0)
                    {
                        throw new SocketCanNativeException(
                            "epoll_wait(FD)", "EPOLLERR event received for CAN socket", (uint)Libc.Errno());
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            HandleBackgroundException(ex);
        }
    }

    private void DrainReceive()
    {
        var useSw = _useSoftwareFilter;
        var pred = _softwareFilterPredicate;

        while (true)
        {
            int gotBatch = 0;
            foreach (var rec in _transceiver.Receive(this, 64))
            {
                gotBatch++;
                var frame = rec.CanFrame;
                bool isErr = frame is CanClassicFrame { IsErrorFrame: true } ||
                             frame is CanFdFrame { IsErrorFrame: true };

                if (isErr)
                {
                    uint raw = frame.RawID;
                    uint err = raw & Libc.CAN_ERR_MASK;
                    var span = frame.Data.Span;
                    var sysTs = Options.PreferKernelTimestamp && rec.ReceiveTimestamp != TimeSpan.Zero
                        ? new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                            .Add(rec.ReceiveTimestamp).UtcDateTime
                        : DateTime.Now;
                    var info = new DefaultCanErrorInfo(
                        SocketCanErr.ToFrameErrorType(err),
                        SocketCanErr.ToControllerStatus(span),
                        SocketCanErr.ToProtocolViolationType(span),
                        SocketCanErr.ToErrorLocation(span),
                        sysTs,
                        err,
                        rec.ReceiveTimestamp,
                        SocketCanErr.InferFrameDirection(err, span),
                        SocketCanErr.ToArbitrationLostBit(err, span),
                        SocketCanErr.ToTransceiverStatus(span),
                        SocketCanErr.ToErrorCounters(err, span),
                        frame);
                    var errSnap = Volatile.Read(ref _errorOccurred);
                    errSnap?.Invoke(this, info);
                }
                else
                {
                    if (useSw && pred is not null && !pred(frame))
                        continue;
                    var evSnap = Volatile.Read(ref _frameReceived);
                    evSnap?.Invoke(this, rec);
                    if (Volatile.Read(ref _asyncConsumerCount) > 0)
                    {
                        _asyncRx.Publish(rec);
                    }
                }
            }
            if (gotBatch < 64) break; // drained
        }
    }

    public event EventHandler<Exception>? BackgroundExceptionOccurred;

    private void HandleBackgroundException(Exception ex)
    {
        try
        {
            CanKitLogger.LogError($"SocketCAN occured background exception on '{_ifName}'.", ex);
        }
        catch { }

        try { _asyncRx.ExceptionOccured(ex); } catch { }

        try
        {
            var snap = Volatile.Read(ref BackgroundExceptionOccurred);
            snap?.Invoke(this, ex);
        }
        catch { }
    }
}
