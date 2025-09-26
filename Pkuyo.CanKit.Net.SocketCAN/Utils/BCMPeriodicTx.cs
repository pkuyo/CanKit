using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.SocketCAN.Native;

namespace Pkuyo.CanKit.Net.SocketCAN.Utils;

public sealed class BCMPeriodicTx : IPeriodicTx
{
    public BCMPeriodicTx(ICanBus bus, CanTransmitData frame, PeriodicTxOptions options, SocketCanBusRtConfigurator configurator)
    {
        _configurator = configurator;
        _fd = Libc.socket(Libc.AF_CAN, Libc.SOCK_DGRAM, Libc.CAN_BCM);
        if (_fd < 0)
        {
            Libc.ThrowErrno("socket(AF_CAN, SOCK_DGRAM, CAN_BCM)", "Failed to create BCM socket");
        }
        var addr = new Libc.sockaddr_can { can_family = (ushort)Libc.AF_CAN, can_ifindex = configurator.ChannelIndex };
        var size = Marshal.SizeOf<Libc.sockaddr_can>();
        if (Libc.connect(_fd, ref addr, size) < 0)
        {
            Libc.ThrowErrno("connect(SOCKADDR_CAN)", $"Failed to connect BCM socket to '{configurator.ChannelIndex}'");
        }

        // Validate frame type vs channel protocol
        if (frame.CanFrame is CanClassicFrame && configurator.ProtocolMode != CanProtocolMode.Can20)
            throw new CanFeatureNotSupportedException(CanFeature.CanClassic, configurator.Features);

        if (frame.CanFrame is CanFdFrame && configurator.ProtocolMode != CanProtocolMode.CanFd)
            throw new CanFeatureNotSupportedException(CanFeature.CanFd, configurator.Features);

        // Configure timers
        var period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
        var ival1 = (options.Repeat < 0) ? TimeSpan.Zero : period;
        var ival2 = (options.Repeat < 0) ? period : TimeSpan.Zero;

        var head = new Libc.bcm_msg_head
        {
            opcode = Libc.TX_SETUP,
            flags = Libc.SETTIMER | Libc.STARTTIMER,
            count = (options.Repeat < 0) ? 0u : (uint)options.Repeat, // 0 = infinite loop
            ival1 = Libc.ToTimeval(ival1),
            ival2 = Libc.ToTimeval(ival2),
            can_id = frame.CanFrame.RawID,
            nframes = 1
        };
        _frame = frame.CanFrame;
        RepeatCount = options.Repeat;
        Period = period;

        var headSize = Marshal.SizeOf<Libc.bcm_msg_head>();
        //use max frame size
        var frameSize = Marshal.SizeOf<Libc.canfd_frame>();
        unsafe
        {
            var buf = stackalloc byte[headSize + frameSize];
            if (frame.CanFrame is CanClassicFrame classic)
            {
                frameSize = Marshal.SizeOf<Libc.can_frame>();
                var frameData = classic.ToCanFrame();
                Buffer.MemoryCopy(&frameData, buf + headSize, frameSize, frameSize);
            }
            else if (frame.CanFrame is CanFdFrame fd)
            {
                var frameData = fd.ToCanFrame();
                Buffer.MemoryCopy(&frameData, buf + headSize, frameSize, frameSize);
            }
            else
            {
                throw new NotSupportedException("protocol mode not supported");
            }

            Buffer.MemoryCopy(&head, buf, headSize, headSize);
            var wrote = Libc.write(_fd, buf, (ulong)(headSize + frameSize));
            if (wrote != headSize + frameSize)
            {
                Libc.ThrowErrno("write(BCM TX_SETUP)", "Failed to setup BCM periodic transmission");
            }
        }
    }

    public void Update(CanTransmitData? frame = null, TimeSpan? period = null, int? repeatCount = null)
    {
        if (_fd < 0) throw new CanBusDisposedException();

        if (period is not null)
            Period = period.Value <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : period.Value;

        var newCount = 0;
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
            _frame = frame.CanFrame;

        var flags = 0u;
        if (period is not null) flags |= Libc.SETTIMER; // update timers
        if (repeatCount is not null) flags |= Libc.STARTTIMER; // restart sequence when count updated

        // Configure timers
        var ival1 = (RepeatCount < 0) ? TimeSpan.Zero : Period;
        var ival2 = (RepeatCount < 0) ? Period : TimeSpan.Zero;

        var head = new Libc.bcm_msg_head
        {
            opcode = Libc.TX_SETUP,
            flags = flags,
            count = (RepeatCount < 0) ? 0u : (uint)newCount,
            ival1 = Libc.ToTimeval(ival1),
            ival2 = Libc.ToTimeval(ival2),
            can_id = _frame.RawID,
            nframes = (uint)(frame is null ? 0 : 1)
        };

        var headSize = Marshal.SizeOf<Libc.bcm_msg_head>();
        var maxFrameSize = Marshal.SizeOf<Libc.canfd_frame>();
        var bufSize = headSize + (frame is null ? 0 : maxFrameSize);
        unsafe
        {
            var buf = stackalloc byte[bufSize];
            if (frame is not null)
            {
                if (frame.CanFrame is CanClassicFrame classic)
                {
                    var size = Marshal.SizeOf<Libc.can_frame>();
                    var frameData = classic.ToCanFrame();
                    Buffer.MemoryCopy(&frameData, buf + headSize, size, size);
                }
                else if (frame.CanFrame is CanFdFrame fd)
                {
                    var size = Marshal.SizeOf<Libc.canfd_frame>();
                    var frameData = fd.ToCanFrame();
                    Buffer.MemoryCopy(&frameData, buf + headSize, size, size);
                }
                else
                {
                    throw new NotSupportedException("protocol mode not supported");
                }
            }

            Buffer.MemoryCopy(&head, buf, headSize, headSize);
            var wrote = Libc.write(_fd, buf, (ulong)bufSize);
            if (wrote != bufSize)
            {
                Libc.ThrowErrno("write(BCM TX_SETUP)", "Failed to update BCM periodic transmission");
            }
        }
        // If previously completed and monitor stopped, ensure it runs again
        EnsureMonitorIfNeeded();
    }

    public int RemainingCount
    {
        get
        {
            Libc.bcm_msg_head head = new Libc.bcm_msg_head
            {
                opcode = Libc.TX_READ,
                can_id = _frame.RawID,
                nframes = 0
            };

            unsafe
            {
                var headSize = (uint)Marshal.SizeOf<Libc.bcm_msg_head>();
                if (Libc.write(_fd, &head, headSize) < 0)
                {
                    //TODO:
                    Libc.ThrowErrno("","");
                }

                var n = Libc.read(_fd,&head, headSize);
                if (n != headSize)
                {
                    //TODO:
                    Libc.ThrowErrno("","");
                }

                if (head.opcode != Libc.TX_STATUS)
                {
                    //TODO:异常处理
                }

                return (int)head.count;
            }

        }
    }

    public void Stop()
    {
        if (_fd < 0)
        {
            return;
        }

        try
        {
            var head = new Libc.bcm_msg_head
            {
                opcode = Libc.TX_DELETE,
                flags = 0,
                count = 0,
                ival1 = default,
                ival2 = default,
                can_id = _frame.RawID,
                nframes = 0
            };
            var size = Marshal.SizeOf<Libc.bcm_msg_head>();
            unsafe
            {
                var buf = stackalloc byte[size];
                Buffer.MemoryCopy(&head, buf, size, size);
                var wrote = Libc.write(_fd, buf, (ulong)size);
                if (wrote != size)
                {
                    Libc.ThrowErrno("write(BCM TX_DELETE)", "Failed to delete BCM periodic transmission");
                }
            }
        }
        catch
        {
            // ignore errors on stop
        }
        finally
        {
            try { Libc.close(_fd); } catch { }
            _fd = -1;
            StopMonitor();
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
                CanKitLogger.LogWarning("The Completed event will never be raised in infinite repeat mode (RepeatCount < 0)." +
                                        " Set RepeatCount to a non-negative value to enable completion notifications.");
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
        if (_fd < 0 || _readTask is { IsCompleted: false })
        {
            return;
        }
        // Skip when infinite repeat (-1) as it will never expire
        if (RepeatCount < 0)
        {
            return;
        }
        // Enable TX_EXPIRED notification only when someone listens
        TryEnableCountEvent();
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        _readTask = Task.Factory.StartNew(() => ReadLoop(token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void EnsureMonitorIfNeeded()
    {
        if (_completed is null) return;
        if (_fd < 0) return;
        if (RepeatCount < 0) return; // infinite => no completion
        if (_readTask is { IsCompleted: false }) return;
        StartMonitor();
    }

    private void StopMonitor()
    {
        try
        {
            lock (_evtGate)
            {
                _readCts?.Cancel();
            }

            _readTask?.Wait(100);
        } catch { }
        finally { _readTask = null; _readCts?.Dispose(); _readCts = null; }
    }

    private void TryEnableCountEvent()
    {
        try
        {
            var head = new Libc.bcm_msg_head
            {
                opcode = Libc.TX_SETUP,
                flags = Libc.TX_COUNTEVT,
                count = 0,
                ival1 = default,
                ival2 = default,
                can_id = _frame.RawID,
                nframes = 0
            };
            var size = Marshal.SizeOf<Libc.bcm_msg_head>();
            unsafe
            {
                var buf = stackalloc byte[size];
                Buffer.MemoryCopy(&head, buf, size, size);
                _ = Libc.write(_fd, buf, (ulong)size);
            }
        }
        catch { /* ignore */ }
    }

    private unsafe void ReadLoop(CancellationToken token)
    {
        var headSize = Marshal.SizeOf<Libc.bcm_msg_head>();
        var buf = stackalloc byte[headSize];
        while (!token.IsCancellationRequested && _fd >= 0)
        {
            var r = Libc.read(_fd, buf, (ulong)headSize);
            if (r < 0)
            {
                break; // interrupted or closed
            }
            if (r < headSize)
            {
                continue;
            }

            var head = *(Libc.bcm_msg_head*)buf;
            if (head.opcode == Libc.TX_EXPIRED)
            {
                try { _completed?.Invoke(this, EventArgs.Empty); }
                catch
                {
                    // ignored
                }

                break; // one-shot completion
            }
        }
    }

    private ICanFrame _frame;
    private int _fd;
    private IBusRTOptionsConfigurator _configurator;

    private readonly object _evtGate = new();
    private EventHandler? _completed;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
}
