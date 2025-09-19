using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.SocketCAN.Native;

namespace Pkuyo.CanKit.SocketCAN;

public sealed class SocketCanChannel : ICanChannel<SocketCanChannelRTConfigurator>, ICanApplier
{
    internal SocketCanChannel(SocketCanDevice device, IChannelOptions options, ITransceiver transceiver)
    {
        Options = new SocketCanChannelRTConfigurator();
        Options.Init((SocketCanChannelOptions)options);
        _device = device;

        // Create socket & bind immediately; Open() will only mark logical state
        _fd = CreateAndBind(Options.InterfaceName, Options.ProtocolMode);

        // Apply initial options (filter / protocol enabling etc.)
        options.Apply(this, true);

        _transceiver = transceiver;
    }

    private static int CreateAndBind(string ifName, CanProtocolMode mode)
    {
        // create raw socket
        var fd = Libc.socket(Libc.AF_CAN, Libc.SOCK_RAW, Libc.CAN_RAW);
        if (fd < 0) throw new CanChannelCreationException("socket(AF_CAN, SOCK_RAW, CAN_RAW) failed.");

        try
        {
            // enable FD frames if needed
            if (mode == CanProtocolMode.CanFd)
            {
                int on = 1;
                if (Libc.setsockopt(fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FD_FRAMES, ref on, (uint)Marshal.SizeOf<int>()) != 0)
                {
                    Libc.close(fd);
                    throw new CanChannelCreationException("setsockopt(CAN_RAW_FD_FRAMES) failed; kernel may not support CAN FD.");
                }
            }

            // bind to interface
            uint ifIndex = Libc.if_nametoindex(ifName);
            if (ifIndex == 0)
            {
                Libc.close(fd);
                throw new CanChannelCreationException($"Interface '{ifName}' not found.");
            }

            var addr = new Libc.sockaddr_can
            {
                can_family = (ushort)Libc.AF_CAN,
                can_ifindex = unchecked((int)ifIndex),
            };

            if (Libc.bind(fd, ref addr, Marshal.SizeOf<Libc.sockaddr_can>()) != 0)
            {
                Libc.close(fd);
                throw new CanChannelCreationException($"bind({ifName}) failed.");
            }

            return fd;
        }
        catch
        {
            try { Libc.close(fd); } catch { /* ignored */ }
            throw;
        }
    }

    public void Open()
    {
        ThrowIfDisposed();
        _isOpen = true;
    }

    public void Reset()
    {
        ThrowIfDisposed();
        _isOpen = false;
    }

    public void Close()
    {
        Reset();
    }

    public void ClearBuffer()
    {
        ThrowIfDisposed();
        // Drain with non-blocking reads based on FIONREAD
        int bytes = 0;
        if (_fd >= 0 && Libc.ioctl(_fd, Libc.FIONREAD, ref bytes) == 0 && bytes > 0)
        {
            var buf = Marshal.AllocHGlobal(bytes);
            try { _ = Libc.read(_fd, buf, (ulong)bytes); }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    public uint Transmit(params CanTransmitData[] frames)
    {
        ThrowIfDisposed();
        if (!_isOpen) throw new CanChannelNotOpenException();
        return _transceiver.Transmit(this, frames);
    }

    public float BusUsage()
    {
        throw new CanFeatureNotSupportedException(CanFeature.BusUsage, Options.Provider.Features);
    }

    public CanErrorCounters ErrorCounters()
    {
        throw new CanFeatureNotSupportedException(CanFeature.ErrorCounters, Options.Provider.Features);
    }

    public IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1)
    {
        ThrowIfDisposed();
        if (!_isOpen) throw new CanChannelNotOpenException();

        // Dispatch to appropriate read path via transceiver to respect frame type
        return _transceiver switch
        {
            SocketCanClassicTransceiver => ReadClassic(count, timeOut),
            SocketCanFdTransceiver => ReadFd(count, timeOut),
            _ => []
        };
    }

    public bool ReadChannelErrorInfo(out ICanErrorInfo? errorInfo)
    {
        // SocketCAN via raw socket does not expose detailed error info here
        errorInfo = null;
        return false;
    }

    public uint GetReceiveCount()
    {
        ThrowIfDisposed();
        int bytes = 0;
        if (_fd < 0) return 0;
        if (Libc.ioctl(_fd, Libc.FIONREAD, ref bytes) != 0 || bytes <= 0) return 0;

        var unit = _transceiver is SocketCanFdTransceiver ? Marshal.SizeOf<Libc.canfd_frame>() : Marshal.SizeOf<Libc.can_frame>();
        if (unit <= 0) return 0;
        return (uint)Math.Max(0, bytes / unit);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {
            StopPolling();
            if (_fd >= 0)
            {
                try { Libc.close(_fd); } catch { /* ignore */ }
            }
        }
        finally
        {
            _fd = -1;
            _isOpen = false;
            _isDisposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanChannelDisposedException();
    }

    public void Apply(ICanOptions options)
    {
        if (options is not SocketCanChannelOptions sc)
            throw new CanOptionTypeMismatchException(
                CanKitErrorCode.ChannelOptionTypeMismatch,
                typeof(SocketCanChannelOptions),
                options?.GetType() ?? typeof(IChannelOptions),
                $"channel {Options.ChannelIndex}");

        // Protocol: enable FD if needed (already set at creation). No further action.

        // Filters: only mask filters are supported directly
        if (sc.Filter.filterRules.Count > 0)
        {
            if (sc.Filter.filterRules[0] is FilterRule.Mask mask)
            {
                if (sc.Filter.filterRules.Count > 1)
                    throw new CanFilterConfigurationException("SocketCAN channel only supports a single mask rule in this wrapper.");

                var cf = new Libc.can_filter
                {
                    can_id = mask.AccCode,
                    can_mask = mask.AccMask
                };

                var size = Marshal.SizeOf<Libc.can_filter>();
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(cf, ptr, false);
                    if (Libc.setsockopt(_fd, Libc.SOL_CAN_RAW, Libc.CAN_RAW_FILTER, ptr, (uint)size) != 0)
                    {
                        throw new CanChannelConfigurationException("setsockopt(CAN_RAW_FILTER) failed.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            else
            {
                throw new CanFilterConfigurationException("SocketCAN wrapper does not support range filters.");
            }
        }
    }

    public CanOptionType ApplierStatus => _fd >= 0 ? CanOptionType.Runtime : CanOptionType.Init;

    // Classic read/write helpers
    internal uint WriteClassic(params CanTransmitData[] frames)
    {
        uint ok = 0;
        foreach (var f in frames)
        {
            if (f.CanFrame is not CanClassicFrame cf) continue;
            var frame = new Libc.can_frame
            {
                can_id = cf.RawID,
                can_dlc = cf.Dlc,
                __pad = 0,
                __res0 = 0,
                __res1 = 0,
                data = new byte[8]
            };
            var src = cf.Data.Span;
            for (int i = 0; i < src.Length && i < 8; i++) frame.data[i] = src[i];

            var size = Marshal.SizeOf<Libc.can_frame>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(frame, ptr, false);
                var n = Libc.write(_fd, ptr, (ulong)size);
                if (n == size) ok++;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        return ok;
    }

    internal IEnumerable<CanReceiveData> ReadClassic(uint count, int timeout)
    {
        var result = new List<CanReceiveData>((int)count);
        var pollfd = new Libc.pollfd { fd = _fd, events = Libc.POLLIN };
        if (Libc.poll(ref pollfd, 1, timeout) <= 0)
            return result;

        int unit = Marshal.SizeOf<Libc.can_frame>();
        for (uint i = 0; i < count; i++)
        {
            var buf = Marshal.AllocHGlobal(unit);
            try
            {
                var n = Libc.read(_fd, buf, (ulong)unit);
                if (n != unit) break;
                var frame = Marshal.PtrToStructure<Libc.can_frame>(buf);
                var data = new byte[frame.can_dlc];
                Array.Copy(frame.data ?? Array.Empty<byte>(), data, Math.Min(data.Length, 8));
                result.Add(new CanReceiveData(new CanClassicFrame(frame.can_id, data))
                {
                    recvTimestamp = 0
                });
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return result;
    }

    // FD read/write helpers
    internal uint WriteFd(params CanTransmitData[] frames)
    {
        uint ok = 0;
        foreach (var f in frames)
        {
            if (f.CanFrame is not CanFdFrame ff) continue;
            var frame = new Libc.canfd_frame
            {
                can_id = ff.RawID,
                len = (byte)CanFdFrame.DlcToLen(ff.Dlc),
                flags = (byte)(ff.BitRateSwitch ? 1 : 0),
                __res0 = 0,
                __res1 = 0,
                data = new byte[64]
            };
            var src = ff.Data.Span;
            for (int i = 0; i < src.Length && i < 64; i++) frame.data[i] = src[i];

            var size = Marshal.SizeOf<Libc.canfd_frame>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(frame, ptr, false);
                var n = Libc.write(_fd, ptr, (ulong)size);
                if (n == size) ok++;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        return ok;
    }

    internal IEnumerable<CanReceiveData> ReadFd(uint count, int timeout)
    {
        var result = new List<CanReceiveData>((int)count);
        var pollfd = new Libc.pollfd { fd = _fd, events = Libc.POLLIN };
        if (Libc.poll(ref pollfd, 1, timeout) <= 0)
            return result;

        int unit = Marshal.SizeOf<Libc.canfd_frame>();
        for (uint i = 0; i < count; i++)
        {
            var buf = Marshal.AllocHGlobal(unit);
            try
            {
                var n = Libc.read(_fd, buf, (ulong)unit);
                if (n != unit) break;
                var frame = Marshal.PtrToStructure<Libc.canfd_frame>(buf);
                var data = new byte[frame.len];
                Array.Copy(frame.data, data, Math.Min(data.Length, 64));
                result.Add(new CanReceiveData(new CanFdFrame(frame.can_id, data))
                {
                    recvTimestamp = 0
                });
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return result;
    }

    private void StartPollingIfNeeded()
    {
        if (_pollTask is { IsCompleted: false }) return;
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _pollTask = Task.Factory.StartNew(
            () => PollLoop(token), token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private void StopPolling()
    {
        try
        {
            _pollCts?.Cancel();
            _pollTask?.Wait(200);
        }
        catch
        {
            // ignored
        }
        finally
        {
            _pollTask = null;
            _pollCts?.Dispose();
            _pollCts = null;
        }
    }

    private void PollLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!_isOpen)
            {
                Thread.Sleep(20);
                continue;
            }
            if (Volatile.Read(ref _subscriberCount) <= 0) break;
            
            //TODO:epoll
        }
    }

    public bool IsOpen => _isOpen;

    public SocketCanChannelRTConfigurator Options { get; }

    IChannelRTOptionsConfigurator ICanChannel.Options => Options;

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

    private readonly SocketCanDevice _device;
    private readonly ITransceiver _transceiver;
    private bool _isDisposed;
    private bool _isOpen;
    private int _fd = -1;

    private readonly object _evtGate = new();
    private EventHandler<CanReceiveData>? _frameReceived;
    private EventHandler<ICanErrorInfo>? _errorOccurred;
    private int _subscriberCount;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
}
