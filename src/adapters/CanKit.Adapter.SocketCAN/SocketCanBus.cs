using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Adapter.SocketCAN.Diagnostics;
using CanKit.Adapter.SocketCAN.Definitions;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Adapter.SocketCAN.Utils;
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
    private FileDescriptorHandle _epfd = new();
    private FileDescriptorHandle _cancelFd = new();
    private CancellationTokenSource? _epollCts;
    private Task? _epollTask;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private Libc.epoll_event[] _events = new Libc.epoll_event[8];
    private FileDescriptorHandle _fd;
    private string _ifName;
    private EventHandler<CanReceiveData>? _frameReceived;

    private bool _isDisposed;

    private IDisposable? _owner;

    // Cached software filter predicate to avoid rebuilding per-iteration
    private Func<CanFrame, bool>? _softwareFilterPredicate;
    private bool _useSoftwareFilter;
    private readonly AsyncFramePipe _asyncRx;
    internal SocketCanBus(IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
    {
        Options = new SocketCanBusRtConfigurator();
        Options.Init((SocketCanBusOptions)options);
        options.Capabilities = ((SocketCanProvider)provider).QueryCapabilities(options);
        options.Features = options.Capabilities.Features;
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

        NativeHandle = new BusNativeHandle(_fd.DangerousGetHandle());

        // Apply socket options
        ApplySocketConfig(options);
        CanKitLogger.LogDebug("SocketCAN: Initial options applied.");

        StartReceiveLoop();
    }

    internal FileDescriptorHandle Handle => _fd!;

    public BusNativeHandle NativeHandle { get; }

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
                else if ((Options.EnabledSoftwareFallback & CanFeature.RangeFilter) != 0)
                {
                    sc.Filter.SoftwareFilterRules.Add(r);
                }
            }
        }
        var tv = SocketCanUtils.ToTimeval(TimeSpan.FromMilliseconds(Options.ReadTImeOutMs));

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
            if (Libc.setsockopt(_fd, Libc.SOL_SOCKET, Libc.SO_RCVBUF, ref cap, (uint)Marshal.SizeOf<int>()) != Libc.OK)
            {
                throw new CanBusConfigurationException("setsockopt(SO_RCVBUF) failed.");
            }
        }

        if (Options.TransmitBufferCapacity != null)
        {
            var cap = (int)Options.TransmitBufferCapacity.Value;
            if (Libc.setsockopt(_fd, Libc.SOL_SOCKET, Libc.SO_SNDBUF, ref cap, (uint)Marshal.SizeOf<int>()) != Libc.OK)
            {
                throw new CanBusConfigurationException("setsockopt(SO_SNDBUF) failed.");
            }
        }

        // Cache software filter predicate for event loop
        _useSoftwareFilter = (Options.EnabledSoftwareFallback & CanFeature.RangeFilter) != 0
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


    public int Transmit(IEnumerable<CanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        int sendCount = 0;
        var stopWatch = new Stopwatch();
        var pollFd = new Libc.pollfd { fd = _fd.DangerousGetHandle().ToInt32(), events = Libc.POLLOUT };
        var needSend = frames.ToArray();
        var wrote = _transceiver.Transmit(this, needSend.AsSpan());
        sendCount = wrote;
        var remainingTime = timeOut;
        while ((timeOut < 0 || remainingTime > 0) && sendCount < needSend.Length)
        {
            remainingTime = timeOut > 0
                ? Math.Max(0, timeOut - (int)(stopWatch.Elapsed.TotalMilliseconds))
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
                new ArraySegment<CanFrame>(needSend, sendCount, needSend.Length - sendCount).AsSpan());
            sendCount += wrote;

        }

        return sendCount;

    }

    public int Transmit(ReadOnlySpan<CanFrame> frames, int timeOut = 0)
    {
        ThrowIfDisposed();
        int sendCount = 0;
        var stopWatch = new Stopwatch();
        var pollFd = new Libc.pollfd { fd = _fd.DangerousGetHandle().ToInt32(), events = Libc.POLLOUT };
        var needSend = frames.ToArray();
        var wrote = _transceiver.Transmit(this, needSend.AsSpan());
        sendCount = wrote;
        var remainingTime = timeOut;
        while ((timeOut < 0 || remainingTime > 0) && sendCount < needSend.Length)
        {
            remainingTime = timeOut > 0
                ? (int)Math.Max(0, timeOut - stopWatch.ElapsedMilliseconds)
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
                new ArraySegment<CanFrame>(needSend, sendCount, needSend.Length - sendCount).AsSpan());
            sendCount += wrote;

        }

        return sendCount;

    }

    public int Transmit(CanFrame[] frames, int timeOut = 0)
        => Transmit(frames.AsSpan(), timeOut);

    public int Transmit(ArraySegment<CanFrame> frames, int timeOut = 0)
        => Transmit(frames.AsSpan(), timeOut);

    public int Transmit(in CanFrame frame)
        => _transceiver.Transmit(this, frame);

    public IEnumerable<CanReceiveData> Receive(int count = 1, int timeOut = 0)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        // To prevent cross-handler contention when subscribing to FrameReceived or ErrorFrameReceived, handle all messages asynchronously.
        return ReceiveAsync(count, timeOut).GetAwaiter().GetResult();
    }

    public Task<int> TransmitAsync(IEnumerable<CanFrame> frames, int timeOut = 0, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            try { return Transmit(frames, timeOut); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { HandleBackgroundException(ex); throw; }
        }, cancellationToken);

    public Task<int> TransmitAsync(CanFrame frame, CancellationToken cancellationToken = default)
        => Task.FromResult(Transmit(frame));

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveAsync(int count = 1, int timeOut = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return await _asyncRx.ReceiveBatchAsync(count, timeOut, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<CanReceiveData> GetFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public void ClearBuffer() => throw new NotSupportedException("SocketCAN does not support clear buffer");

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

    public IPeriodicTx TransmitPeriodic(CanFrame frame, PeriodicTxOptions options)
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
            _fd.Dispose();
        }
        finally
        {
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

            lock (_evtGate)
            {
                _frameReceived += value;
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _frameReceived -= value;
            }
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
            lock (_evtGate)
            {
                _errorOccurred += value;
            }
        }
        remove
        {
            if (!Options.AllowErrorInfo)
            {
                throw new CanBusConfigurationException("ErrorOccurred subscription requires AllowErrorInfo=true in options.");
            }
            lock (_evtGate)
            {
                _errorOccurred -= value;
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
                var errno = Libc.Errno();
                CanKitLogger.LogWarning($"SocketCAN: can_get_state failed for '{_ifName}', errno={errno}.");
                return BusState.Unknown;
            }
        }
    }

    private FileDescriptorHandle CreateAndBind(string? ifName, CanProtocolMode mode, bool preferKernelTs)
    {
        // create raw socket
        var fd = Libc.socket(Libc.AF_CAN, Libc.SOCK_RAW, Libc.CAN_RAW);
        if (fd.IsInvalid)
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
                    fd.Dispose();
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
                fd.Dispose();
                throw new CanBusCreationException($"bind({ifName}) failed.");
            }

            return fd;
        }
        catch
        {
            try { fd.Dispose(); } catch { /* ignored */ }
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
                CanKitLogger.LogWarning(
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

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }


    private void StartReceiveLoop()
    {
        _epfd = Libc.epoll_create1(Libc.EPOLL_CLOEXEC);
        if (_epfd.IsInvalid)
        {
            Libc.ThrowErrno("epoll_create1", "Failed to create epoll instance");
        }

        // create cancel eventfd for immediate epoll wake on stop
        _cancelFd = Libc.eventfd(0, Libc.EFD_NONBLOCK | Libc.EFD_CLOEXEC);
        if (_cancelFd.IsInvalid)
        {
            Libc.ThrowErrno("eventfd", "Failed to create eventfd for cancellation");
        }
#if NET8_0_OR_GREATER
        var ev = new Libc.epoll_event { events = Libc.EPOLLIN, data = _fd.DangerousGetHandle() };
#else
        var ev = new Libc.epoll_event { events = Libc.EPOLLIN | Libc.EPOLLERR, data = _fd.DangerousGetHandle() };
#endif
        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _fd, ref ev) < 0)
        {
            Libc.ThrowErrno("epoll_ctl(EPOLL_CTL_ADD)", "failed to add fd to epoll instance");
        }

        // add cancel fd to epoll
        var evCancel = new Libc.epoll_event { events = Libc.EPOLLIN, data = _cancelFd.DangerousGetHandle() };
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
        var cts = Volatile.Read(ref _epollCts);
        var cancelFd = Volatile.Read(ref _cancelFd);
        var task = Volatile.Read(ref _epollTask);
        var epFd = Volatile.Read(ref _epfd);
        try
        {
            _asyncRx.Clear();
            cts?.Cancel();
            // wake epoll_wait immediately via eventfd
            try
            {
                if (!cancelFd.IsInvalid)
                {
                    unsafe
                    {
                        ulong one = 1UL;
                        _ = Libc.write(_cancelFd, &one, (ulong)sizeof(ulong));
                    }
                }
            }
            catch { /* ignore wake errors */ }
            task?.Wait(200);
        }
        catch { /*ignored*/ }
        finally
        {
            epFd.Dispose();
            cancelFd.Dispose();
            Interlocked.CompareExchange(ref _epollTask, task, null);
            Interlocked.CompareExchange(ref _epollCts, cts, null);
            cts?.Dispose();
            CanKitLogger.LogDebug("SocketCAN: epoll loop stopped.");
        }
    }


    private void EPollLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
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
                    if (fd == _cancelFd.DangerousGetHandle().ToInt32())
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

                    if ((_events[i].events & Libc.EPOLLIN) != 0 && fd == _fd.DangerousGetHandle().ToInt32())
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
            foreach (var rec in _transceiver.Receive(this, Libc.BATCH_COUNT))
            {
                gotBatch++;
                var frame = rec.CanFrame;
                if (frame.IsErrorFrame)
                {
                    uint raw = frame.ToCanID();
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
                        SocketCanErr.ToTransceiverStatus(span),
                        sysTs,
                        err,
                        rec.ReceiveTimestamp,
                        SocketCanErr.InferFrameDirection(err, span),
                        SocketCanErr.ToArbitrationLostBit(err, span),
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
                    _asyncRx.Publish(rec);
                }
            }
            if (gotBatch == 0) break; // drained
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
