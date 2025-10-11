using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Adapter.SocketCAN.Diagnostics;
using CanKit.Adapter.SocketCAN.Native;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.SocketCAN.Utils;


public sealed class BCMPeriodicTx : IPeriodicTx
{

    private readonly object _evtGate = new();
    private EventHandler? _completed;

    private SocketCanBusRtConfigurator _configurator;
    private int _fd;
    private int _queryFd = -1;
    private int _cancelFd = -1;
    private int _epfd = -1;
    private Libc.epoll_event[] _events = new Libc.epoll_event[4];

    private ICanFrame _frame;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public BCMPeriodicTx(
        ICanBus bus,
        CanTransmitData frame,
        PeriodicTxOptions options,
        SocketCanBusRtConfigurator configurator)
    {
        _configurator = configurator;

        _fd = Libc.socket(Libc.AF_CAN, Libc.SOCK_DGRAM, Libc.CAN_BCM);
        if (_fd < 0)
            Libc.ThrowErrno("socket(AF_CAN, SOCK_DGRAM, CAN_BCM)", "Failed to create BCM socket");

        var addr = new Libc.sockaddr_can { can_family = (ushort)Libc.AF_CAN, can_ifindex = configurator.ChannelIndex };
        var saSize = Marshal.SizeOf<Libc.sockaddr_can>();
        if (Libc.connect(_fd, ref addr, saSize) < 0)
            Libc.ThrowErrno("connect(SOCKADDR_CAN)", $"Failed to connect BCM socket to '{configurator.ChannelIndex}'");
        TrySetNonBlocking(_fd);


        _queryFd = Libc.socket(Libc.AF_CAN, Libc.SOCK_DGRAM, Libc.CAN_BCM);
        if (_queryFd < 0)
            Libc.ThrowErrno("socket(AF_CAN, SOCK_DGRAM, CAN_BCM)", "Failed to create BCM query socket");
        if (Libc.connect(_queryFd, ref addr, saSize) < 0)
            Libc.ThrowErrno("connect(SOCKADDR_CAN)", $"Failed to connect BCM query socket to '{configurator.ChannelIndex}'");
        TrySetNonBlocking(_queryFd);


        _cancelFd = Libc.eventfd(0, Libc.EFD_CLOEXEC | Libc.EFD_NONBLOCK);
        if (_cancelFd < 0)
            Libc.ThrowErrno("eventfd", "Failed to create cancel eventfd");

        if (frame.CanFrame is CanClassicFrame && configurator.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanClassic, configurator.Features);
        if (frame.CanFrame is CanFdFrame && configurator.ProtocolMode != CanProtocolMode.CanFd)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, configurator.Features);


        var period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        var ival1 = (options.Repeat < 0) ? TimeSpan.Zero : period; // repeat config times and stop
        var ival2 = (options.Repeat < 0) ? period : TimeSpan.Zero; // immediately enter ival2 infinite inf loop

        var head = new Libc.bcm_msg_head
        {
            opcode = Libc.TX_SETUP,
            flags = Libc.SETTIMER | Libc.STARTTIMER | Libc.TX_COUNTEVT
                    | (frame.CanFrame is CanFdFrame ? Libc.CAN_FD_FRAME : 0u),
            count = (options.Repeat < 0) ? 0u : (uint)options.Repeat,
            ival1 = Libc.ToTimeval(ival1),
            ival2 = Libc.ToTimeval(ival2),
            can_id = frame.CanFrame.ToCanID(),
            nframes = 1
        };

        _frame = frame.CanFrame;
        RepeatCount = options.Repeat;
        Period = period;

        var headSize = Marshal.SizeOf<Libc.bcm_msg_head>();
        var frameSize = (_frame is CanFdFrame) ? Marshal.SizeOf<Libc.canfd_frame>() : Marshal.SizeOf<Libc.can_frame>();

        unsafe
        {
            var buf = stackalloc byte[headSize + frameSize];

            if (_frame is CanClassicFrame classic)
            {
                var fr = classic.ToCanFrame();
                Buffer.MemoryCopy(&fr, buf + headSize, frameSize, frameSize);
            }
            else if (_frame is CanFdFrame fd)
            {
                var fr = fd.ToCanFrame();
                Buffer.MemoryCopy(&fr, buf + headSize, frameSize, frameSize);
            }
            else
            {
                throw new NotSupportedException("protocol mode not supported");
            }

            Buffer.MemoryCopy(&head, buf, headSize, headSize);
            var wrote = Libc.write(_fd, buf, (ulong)(headSize + frameSize));
            if (wrote != headSize + frameSize)
                Libc.ThrowErrno("write(BCM TX_SETUP)", "Failed to setup BCM periodic transmission");
        }
    }

    public void Update(CanTransmitData? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        if (_fd < 0) throw new CanBusDisposedException();

        if (period is not null)
            Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;

        int newCount;
        if (repeatCount is not null)
        {
            RepeatCount = repeatCount.Value;
            newCount = RepeatCount;
        }
        else
        {
            newCount = RemainingCount;
        }
        if (frame is not null)
        {
            _frame = frame.Value.CanFrame;
        }

        var flags = 0u;
        if (period is not null) flags |= Libc.SETTIMER;
        if (repeatCount is not null) flags |= Libc.STARTTIMER;

        // only set CAN_FD_FRAME when frame has value
        bool includeFrame = frame is not null;
        if (includeFrame && _frame is CanFdFrame)
            flags |= Libc.CAN_FD_FRAME;

        var head = new Libc.bcm_msg_head
        {
            opcode = Libc.TX_SETUP,
            flags = flags,
            count = (RepeatCount < 0) ? 0u : (uint)newCount,
            ival1 = Libc.ToTimeval((RepeatCount < 0) ? TimeSpan.Zero : Period), // repeat config times and stop
            ival2 = Libc.ToTimeval((RepeatCount < 0) ? Period : TimeSpan.Zero), // immediately enter ival2 for infinite loop
            can_id = _frame.ToCanID(),
            nframes = (uint)(includeFrame ? 1 : 0)
        };

        var headSize = Marshal.SizeOf<Libc.bcm_msg_head>();
        var maxFrameSize = Marshal.SizeOf<Libc.canfd_frame>();
        var bufSize = headSize + (includeFrame ? maxFrameSize : 0);

        unsafe
        {
            var buf = stackalloc byte[bufSize];

            if (includeFrame)
            {
                if (_frame is CanClassicFrame classic)
                {
                    var sz = Marshal.SizeOf<Libc.can_frame>();
                    var fr = classic.ToCanFrame();
                    Buffer.MemoryCopy(&fr, buf + headSize, sz, sz);
                }
                else if (_frame is CanFdFrame fd)
                {
                    var sz = Marshal.SizeOf<Libc.canfd_frame>();
                    var fr = fd.ToCanFrame();
                    Buffer.MemoryCopy(&fr, buf + headSize, sz, sz);
                }
                else
                {
                    throw new NotSupportedException("protocol mode not supported");
                }
            }

            Buffer.MemoryCopy(&head, buf, headSize, headSize);
            var wrote = Libc.write(_fd, buf, (ulong)bufSize);
            if (wrote != bufSize)
                Libc.ThrowErrno("write(BCM TX_SETUP)", "Failed to update BCM periodic transmission");
        }
        EnsureMonitorIfNeeded();
    }

    public int RemainingCount
    {
        get
        {
            if (_queryFd < 0) throw new CanBusDisposedException();

            Libc.bcm_msg_head head = new Libc.bcm_msg_head
            {
                opcode = Libc.TX_READ,
                can_id = _frame.ToCanID(),
                nframes = 0
            };

            unsafe
            {
                var headSize = (uint)Marshal.SizeOf<Libc.bcm_msg_head>();
                if (Libc.write(_queryFd, &head, headSize) < 0)
                    Libc.ThrowErrno("write(BCM TX_READ)", "Failed to query BCM status");

                var n = Libc.read(_queryFd, &head, headSize);
                if (n != headSize)
                    Libc.ThrowErrno("read(BCM TX_STATUS)", "Failed to read BCM status");

                if (head.opcode != Libc.TX_STATUS)
                {
                    // 按需处理：此处保持最小化，不抛
                }
                return (int)head.count;
            }
        }
    }

    public void Stop()
    {
        if (_fd < 0) return;

        try
        {
            var head = new Libc.bcm_msg_head
            {
                opcode = Libc.TX_DELETE,
                flags = 0,
                count = 0,
                ival1 = default,
                ival2 = default,
                can_id = _frame.ToCanID(),
                nframes = 0
            };
            var size = Marshal.SizeOf<Libc.bcm_msg_head>();
            unsafe
            {
                var buf = stackalloc byte[size];
                Buffer.MemoryCopy(&head, buf, size, size);
                var wrote = Libc.write(_fd, buf, (ulong)size);
                if (wrote != size)
                    Libc.ThrowErrno("write(BCM TX_DELETE)", "Failed to delete BCM periodic transmission");
            }
        }
        catch
        {
            // ignore errors on stop
        }
        finally
        {
            StopMonitor(true);
            TryClose(ref _fd);
            TryClose(ref _queryFd);
            TryClose(ref _cancelFd);
            TryClose(ref _epfd);
        }
    }

    public void Dispose() => Stop();

    public TimeSpan Period { get; private set; }
    public int RepeatCount { get; private set; }

    public event EventHandler? Completed
    {
        add
        {
            if (RepeatCount < 0)
            {
                CanKitLogger.LogWarning("The Completed event will never be raised in infinite repeat mode (RepeatCount < 0). " +
                                        "Set RepeatCount to a non-negative value to enable completion notifications.");
            }
            lock (_evtGate)
            {
                var wasEmpty = _completed is null;
                _completed += value;
                if (wasEmpty)
                {
                    StartMonitor();
                }
            }
        }
        remove
        {
            lock (_evtGate)
            {
                _completed -= value;
                if (_completed is null)
                {
                    StopMonitor();
                }
            }
        }
    }


    private void StartMonitor()
    {
        if (_fd < 0 || _readTask is { IsCompleted: false }) return;
        if (RepeatCount < 0) return;
        EnsureEpoll();

        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        _readTask = Task.Factory.StartNew(() => ReadLoop(token),
                                          token,
                                          TaskCreationOptions.LongRunning,
                                          TaskScheduler.Default);
    }

    private void EnsureMonitorIfNeeded()
    {
        if (_completed is null) return;
        if (_fd < 0) return;
        if (RepeatCount < 0) return; // infinite => no completion
        if (_readTask is { IsCompleted: false }) return;
        StartMonitor();
    }

    private void StopMonitor(bool disposing = false)
    {
        try
        {
            lock (_evtGate)
            {
                _readCts?.Cancel();
                if (_cancelFd >= 0)
                {
                    unsafe
                    {
                        ulong one = 1;
                        _ = Libc.write(_cancelFd, &one, sizeof(ulong));
                    }
                }
            }

            _readTask?.Wait(200);
        }
        catch { /* ignore */ }
        finally
        {
            _readTask = null;
            _readCts?.Dispose();
            _readCts = null;

            TryClose(ref _epfd);
        }
    }

    private void EnsureEpoll()
    {
        if (_epfd >= 0) return;

        _epfd = Libc.epoll_create1(Libc.EPOLL_CLOEXEC);
        if (_epfd < 0)
            Libc.ThrowErrno("epoll_create1", "Failed to create epoll");


        var ev1 = new Libc.epoll_event
        {
            events = Libc.EPOLLIN | Libc.EPOLLERR,
            data = (IntPtr)_fd
        };
        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _fd, ref ev1) < 0)
            Libc.ThrowErrno("epoll_ctl(ADD)", "Failed to add BCM fd to epoll");

        var ev2 = new Libc.epoll_event
        {
            events = Libc.EPOLLIN,
            data = (IntPtr)_cancelFd
        };
        if (Libc.epoll_ctl(_epfd, Libc.EPOLL_CTL_ADD, _cancelFd, ref ev2) < 0)
            Libc.ThrowErrno("epoll_ctl(ADD)", "Failed to add cancel fd to epoll");
    }



    private unsafe void ReadLoop(CancellationToken token)
    {

        int headSize = Marshal.SizeOf<Libc.bcm_msg_head>();
        var buf = stackalloc byte[headSize];
        while (!token.IsCancellationRequested && _fd >= 0 && _epfd >= 0)
        {
            int n = Libc.epoll_wait(_epfd, _events, _events.Length, -1);
            if (n < 0)
            {
                var errno = Libc.Errno();
                if (errno == Libc.EINTR)
                    continue;
                throw new SocketCanNativeException("epoll_wait(FD)", "epoll_wait occured an error", (uint)errno);
            }
            if (n == 0)
            {
                continue;
            }

            for (int i = 0; i < n; i++)
            {
                int fd = (int)_events[i].data;

                if (fd == _cancelFd)
                {
                    ulong tmp;
                    _ = Libc.read(_cancelFd, &tmp, sizeof(ulong));
                    return;
                }

                if ((_events[i].events & Libc.EPOLLERR) != 0)
                {
                    Libc.ThrowErrno("epoll_wait(FD)", "epoll receive an error event");
                }

                if ((_events[i].events & Libc.EPOLLIN) != 0)
                {
                    long r = Libc.read(_fd, buf, (ulong)headSize);
                    if (r < 0)
                    {
                        var errno = Libc.Errno();
                        if (errno is Libc.EINTR or Libc.EAGAIN)
                            continue;
                    }

                    if (r < headSize) continue;

                    var head = *(Libc.bcm_msg_head*)buf;

                    if (head.opcode == Libc.TX_EXPIRED)
                    {
                        try
                        {
                            _completed?.Invoke(this, EventArgs.Empty);
                        }
                        catch
                        { /* ignore */ }
                        return;
                    }
                }
            }
        }
    }

    private static void TrySetNonBlocking(int fd)
    {
        try
        {
            int flags = Libc.fcntl(fd, Libc.F_GETFL, 0);
            if (flags >= 0)
                _ = Libc.fcntl(fd, Libc.F_SETFL, flags | Libc.O_NONBLOCK);
        }
        catch { /* ignore */ }
    }

    private static void TryClose(ref int fd)
    {
        try { if (fd >= 0) Libc.close(fd); } catch { }
        fd = -1;
    }
}
