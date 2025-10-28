using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Endpoints;
using CanKit.Core.Utils;

namespace CanKit.Protocol.IsoTp;

/// <summary>
/// ISO-TP link built on top of an <see cref="ICanBus"/>. Handles segmentation/reassembly and flow control.
/// 使用底层 ICanBus 的 ISO-TP 通道，封装分段/重组与流控。
/// </summary>
public sealed class IsoTpLink : IDisposable
{
    private readonly ICanBus _bus;
    private readonly IsoTpSettings _opt;
    private readonly bool _ownBus;
    private readonly AsyncFramePipe _pipe;
    private readonly int _dlc;

    private event EventHandler<CanReceiveData>? _rxHandler;
    private event EventHandler<Exception>? _bgErrHandler;

    private IsoTpLink(ICanBus bus, IsoTpSettings opt, bool ownBus)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
        _dlc = opt.UseFd ? _opt.FdDlc : _opt.ClassicalDlc;
        _ownBus = ownBus;
        _pipe = new AsyncFramePipe(capacity: 1024);

        _rxHandler = (_, e) =>
        {
            var f = e.CanFrame;
            if (f.IsErrorFrame) return;
            if (f.IsExtendedFrame != _opt.IsExtendedId) return;
            if (f.ID != _opt.RxId) return;
            _pipe.Publish(e);
        };
        _bgErrHandler = (_, ex) => _pipe.ExceptionOccured(ex);

        _bus.FrameReceived += _rxHandler;
        _bus.BackgroundExceptionOccurred += _bgErrHandler;
    }

    /// <summary>
    /// Open a link by endpoint and settings. （通过 Endpoint 与设置打开 ISO-TP 链路）
    /// </summary>
    public static IsoTpLink Open(string endpoint, IsoTpSettings settings, Action<IBusInitOptionsConfigurator>? configure = null)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        bool chainedConfigure(IBusInitOptionsConfigurator conf)
        {
            try
            {
                configure?.Invoke(conf);
            }
            catch
            {
                // user configuration exceptions should bubble later during open
            }

            try
            {
                // Try set narrow filter for RX ID to reduce traffic load
                conf.RangeFilter(settings.RxId, settings.RxId, settings.IdType);
            }
            catch
            {
                try
                {
                    uint accCode = (uint)settings.RxId;
                    uint accMask = settings.IsExtendedId ? 0x1FFFFFFF : 0x7FFu;
                    conf.AccMask(accCode, accMask, settings.IdType);
                }
                catch { /* fallback best-effort */ }
            }

            return true;
        }

        if (!BusEndpointEntry.TryOpen(endpoint, conf => _ = chainedConfigure(conf), out var bus) || bus is null)
            throw new InvalidOperationException($"Failed to open endpoint: {endpoint}");

        return new IsoTpLink(bus, settings, ownBus: true);
    }

    /// <summary>
    /// Create a link on an existing bus (不负责其生命周期)。
    /// </summary>
    public static IsoTpLink Wrap(ICanBus bus, IsoTpSettings settings) => new(bus, settings, ownBus: false);

    public void Dispose()
    {
        try
        {
            if (_rxHandler is not null) _bus.FrameReceived -= _rxHandler;
            if (_bgErrHandler is not null) _bus.BackgroundExceptionOccurred -= _bgErrHandler;
        }
        catch { /* ignore */ }
        finally
        {
            if (_ownBus)
            {
                _bus.Dispose();
            }
        }
    }

    /// <summary>
    /// Send an ISO-TP message (发送完整的 ISO-TP 报文)。
    /// </summary>
    public async Task SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length == 0)
        {
            // Empty SF
            using var sf = BuildSingleFrame(ReadOnlySpan<byte>.Empty);
            _ = TransmitFrame(sf.CanFrame);
            return;
        }

        var overhead = 1 + (_opt.ExtendedAddress.HasValue ? 1 : 0); // PCI + optional ext addr
        var sfMax = Math.Max(0, _dlc - overhead);
        if (!_opt.UseFd && sfMax > 7) sfMax = 7; // classical SF nibble max

        if (payload.Length <= sfMax)
        {
            using var data = BuildSingleFrame(payload.Span);
            _ = TransmitFrame(data.CanFrame);
            return;
        }

        // Multi-frame transfer
        await SendMultiFrameAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Receive one complete ISO-TP message from the bus (作为接收端重组一帧完整 ISO-TP 消息)。
    /// Sends FC on FF automatically.
    /// </summary>
    public async Task<byte[]> ReceiveAsync(int timeoutMs, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        int leftMs() => timeoutMs <= 0 ? -1 : Math.Max(0, timeoutMs - (int)sw.ElapsedMilliseconds);

        while (true)
        {
            var batch = await _pipe.ReceiveBatchAsync(1, leftMs(), cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0) throw new TimeoutException("Timeout waiting for ISO-TP frame.");

            var data = batch[0].CanFrame.Data.Span;
            int idx = 0;
            if (_opt.ExtendedAddress.HasValue)
            {
                if (data.Length < 2 || data[0] != _opt.ExtendedAddress.Value) continue;
                idx = 1;
            }
            if (data.Length < idx + 1) continue;
            var pci = data[idx];
            var pciType = (byte)(pci >> 4);

            if (pciType == 0x0)
            {
                // Single frame
                int len;
                int sfo = 1;
                if (_opt.UseFd && (pci & 0x0F) == 0)
                {
                    if (data.Length < idx + 2) continue;
                    len = data[idx + 1];
                    sfo = 2;
                }
                else
                {
                    len = pci & 0x0F;
                }
                if (data.Length < idx + sfo + len) continue;
                var result = new byte[len];
                unsafe
                {
                    fixed (byte* dst = result)
                    fixed (byte* src = data)
                    {
                        Unsafe.CopyBlockUnaligned(dst, src + idx + sfo, (uint)len);
                    }
                }
                return result;
            }
            else if (pciType == 0x1)
            {
                // First frame
                if (data.Length < idx + 2) continue;
                int totalLen;
                int ffHeader;
                var nibb = (pci & 0x0F);
                if (_opt.UseFd && nibb == 0 && data.Length >= idx + 6)
                {
                    totalLen = (data[idx + 2] << 24) | (data[idx + 3] << 16) | (data[idx + 4] << 8) | data[idx + 5];
                    ffHeader = 6;
                }
                else
                {
                    totalLen = ((pci & 0x0F) << 8) | data[idx + 1];
                    ffHeader = 2;
                }
                if (totalLen <= 0 || totalLen > _opt.MaxMessageSize)
                {
                    // reply OVFL/Abort
                    using var frame = BuildFlowControl(IsoFlowStatus.Overflow, 0, 0);
                    _ = TransmitFrame(frame.CanFrame);
                    throw new InvalidOperationException($"ISO-TP FF length invalid or exceeds limit: {totalLen}");
                }

                var buffer = ArrayPool<byte>.Shared.Rent(totalLen);
                try
                {
                    int filled = 0;
                    var firstDataStart = idx + ffHeader;
                    var copyLen = Math.Min(totalLen, data.Length - firstDataStart);
                    if (copyLen > 0)
                    {
                        unsafe
                        {
                            fixed (byte* dst = buffer)
                            fixed (byte* src = data)
                            {
                                Unsafe.CopyBlockUnaligned(dst, src + firstDataStart, (uint)copyLen);
                            }
                        }
                    }
                    filled += copyLen;

                    // send FlowControl CTS
                    using var frame = BuildFlowControl(IsoFlowStatus.ContinueToSend, _opt.BlockSize, _opt.STmin);
                    _ = TransmitFrame(frame.CanFrame);

                    byte expectedSn = 1;
                    int blockRemain = _opt.BlockSize == 0 ? int.MaxValue : _opt.BlockSize;

                    while (filled < totalLen)
                    {
                        var wait = Math.Min(leftMs(), _opt.N_Cr);
                        if (wait == 0) wait = _opt.N_Cr;
                        var rx = await _pipe.ReceiveBatchAsync(1, wait, cancellationToken).ConfigureAwait(false);
                        if (rx.Count == 0) throw new TimeoutException("Timeout waiting for Consecutive Frame (N_Cr).");
                        var cdata = rx[0].CanFrame.Data.Span;
                        var cidx = _opt.ExtendedAddress.HasValue ? 1 : 0;
                        if (_opt.ExtendedAddress.HasValue)
                        {
                            if (cdata.Length < 2 || cdata[0] != _opt.ExtendedAddress.Value)
                                continue; // ignore unrelated
                        }
                        if (cdata.Length < cidx + 1) continue;
                        var cpi = cdata[cidx];
                        if ((cpi >> 4) != 0x2) continue; // not CF
                        var sn = (byte)(cpi & 0x0F);

                        if (sn != (expectedSn & 0x0F))
                        {
                            throw new InvalidOperationException($"ISO-TP SN mismatch. expected={expectedSn&0x0F}, got={sn}");
                        }

                        var dataStart = cidx + 1;
                        var remain = totalLen - filled;
                        var take = Math.Min(remain, cdata.Length - dataStart);
                        if (take > 0)
                        {
                            unsafe
                            {
                                fixed (byte* dst = buffer)
                                fixed (byte* src = cdata)
                                {
                                    Unsafe.CopyBlockUnaligned(dst+filled, src+dataStart, (uint)take);
                                }
                            }
                        }
                        filled += take;
                        expectedSn = (byte)((expectedSn + 1) & 0x0F);
                        blockRemain--;

                        if (filled >= totalLen) break;

                        if (blockRemain == 0)
                        {
                            // end of block; send next FC
                            using var fc = BuildFlowControl(IsoFlowStatus.ContinueToSend, _opt.BlockSize, _opt.STmin);
                            _ = TransmitFrame(fc.CanFrame);
                            blockRemain = _opt.BlockSize == 0 ? int.MaxValue : _opt.BlockSize;
                        }
                    }

                    var result = new byte[totalLen];
                    Buffer.BlockCopy(buffer, 0, result, 0, totalLen);
                    return result;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            else
            {
                // ignore other frame types until we see SF/FF
                continue;
            }
        }
    }

    /// <summary>
    /// Send request then wait for one complete response. （发送请求并等待一帧完整响应）
    /// </summary>
    public async Task<byte[]> RequestAsync(ReadOnlyMemory<byte> request, int timeoutMs = 2000, CancellationToken cancellationToken = default)
    {
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReceiveAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendMultiFrameAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        int totalLen = payload.Length;

        // First Frame
        int consumed;
        using (var ff = BuildFirstFrame(payload.Span, out consumed))
        {
            TransmitFrame(ff.CanFrame);
        }

        // Wait for Flow Control
        var (fs, bs, stGap) = await WaitFlowControlAsync(_opt.N_Bs, cancellationToken).ConfigureAwait(false);
        if (fs == IsoFlowStatus.Overflow)
            throw new InvalidOperationException("Remote reported FC Overflow/Abort.");

        // Send CFs
        int offset = consumed;
        byte sn = 1;
        int blockRemain = bs == 0 ? int.MaxValue : bs;

        var per = (_dlc) - (1 + (_opt.ExtendedAddress.HasValue ? 1 : 0));
        per = Math.Max(0, per);

        while (offset < totalLen)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var take = Math.Min(per, totalLen - offset);
            using (var cf = BuildConsecutiveFrame(payload.Span.Slice(offset, take), sn))
            {
                TransmitFrame(cf.CanFrame);
            }
            offset += take;
            sn = (byte)((sn + 1) & 0x0F);
            blockRemain--;

            if (offset >= totalLen) break;

            if (stGap > TimeSpan.Zero)
            {
                if (stGap.TotalMilliseconds >= 1)
                    await Task.Delay(stGap, cancellationToken).ConfigureAwait(false);
                else
                    PreciseDelay.Delay(stGap, ct: cancellationToken);
            }

            if (blockRemain == 0)
            {
                // Need next FC
                (fs, bs, stGap) = await WaitFlowControlAsync(_opt.N_Bs, cancellationToken).ConfigureAwait(false);
                if (fs == IsoFlowStatus.Overflow)
                    throw new InvalidOperationException("Remote reported FC Overflow/Abort.");
                blockRemain = bs == 0 ? int.MaxValue : bs;
            }
        }
    }

    private (byte[] buf, int idx) RentBase()
    {
        var buf = ArrayPool<byte>.Shared.Rent(_dlc);
        int idx = 0;
        if (_opt.ExtendedAddress.HasValue)
        {
            buf[idx++] = _opt.ExtendedAddress.Value;
        }
        return (buf, idx);
    }

    private FrameScope BuildSingleFrame(ReadOnlySpan<byte> payload)
    {
        var overhead = 1 + (_opt.ExtendedAddress.HasValue ? 1 : 0);
        var max = Math.Max(0, _dlc - overhead);
        if (!_opt.UseFd && payload.Length > Math.Min(7, max))
            throw new ArgumentOutOfRangeException(nameof(payload), "Payload too large for Single Frame.");
        if (_opt.UseFd && payload.Length > max)
            throw new ArgumentOutOfRangeException(nameof(payload), $"Payload too large for Single Frame with DLC={_dlc}.");

        var (buf, idx0) = RentBase();
        int idx = idx0;
        // SF header encoding
        if (_opt.UseFd && payload.Length > 7)
        {
            buf[idx++] = 0x00; // SF with extended length
            buf[idx++] = (byte)payload.Length;
        }
        else
        {
            buf[idx++] = (byte)(0x00 | (payload.Length & 0x0F));
        }
        if (!payload.IsEmpty)
            payload.CopyTo(buf.AsSpan(idx));

        if (_opt.PadFrames)
        {
            for (int i = idx + payload.Length; i < _dlc; i++) buf[i] = _opt.PadByte;
            return new FrameScope(_opt.UseFd
                    ? new CanFdFrame(_opt.TxId, buf, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                    : new CanClassicFrame(_opt.TxId, buf, _opt.IsExtendedId),
                    buf);
        }
        else
        {
            var used = idx + payload.Length;
            var mem = new ReadOnlyMemory<byte>(buf, 0, used);
            return new FrameScope(_opt.UseFd
                 ? new CanFdFrame(_opt.TxId, mem, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                 : new CanClassicFrame(_opt.TxId, mem, _opt.IsExtendedId),
                  buf);
        }
    }

    private FrameScope BuildFirstFrame(ReadOnlySpan<byte> payload, out int consumed)
    {
        var (buf, idx0) = RentBase();
        int idx = idx0;

        int totalLen = payload.Length;
        int header;
        if (_opt.UseFd && _opt.AllowLargeFdLength && totalLen > 0x0FFF)
        {
            buf[idx] = 0x10;
            buf[idx + 1] = 0x00;
            buf[idx + 2] = (byte)((totalLen >> 24) & 0xFF);
            buf[idx + 3] = (byte)((totalLen >> 16) & 0xFF);
            buf[idx + 4] = (byte)((totalLen >> 8) & 0xFF);
            buf[idx + 5] = (byte)(totalLen & 0xFF);
            header = 6;
        }
        else
        {
            buf[idx] = (byte)(0x10 | ((totalLen >> 8) & 0x0F));
            buf[idx + 1] = (byte)(totalLen & 0xFF);
            header = 2;
        }
        int dataStart = idx + header;
        var canData = Math.Max(0, _dlc - dataStart);
        consumed = Math.Min(canData, payload.Length);
        if (consumed > 0)
            payload.Slice(0, consumed).CopyTo(buf.AsSpan(dataStart));

        if (_opt.PadFrames)
        {
            for (int i = dataStart + consumed; i < _dlc; i++) buf[i] = _opt.PadByte;
            return new FrameScope(_opt.UseFd
                ? new CanFdFrame(_opt.TxId, buf, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                : new CanClassicFrame(_opt.TxId, buf, _opt.IsExtendedId),
                buf);
        }
        else
        {
            int used = dataStart + consumed;
            var mem = new ReadOnlyMemory<byte>(buf, 0, used);
            return new FrameScope(_opt.UseFd
                ? new CanFdFrame(_opt.TxId, mem, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                : new CanClassicFrame(_opt.TxId, mem, _opt.IsExtendedId),
                 buf);
        }
    }

    private FrameScope BuildConsecutiveFrame(ReadOnlySpan<byte> payloadPart, byte sn)
    {
        var (buf, idx0) = RentBase();
        int idx = idx0;
        buf[idx] = (byte)(0x20 | (sn & 0x0F));
        int dataStart = idx + 1;
        if (!payloadPart.IsEmpty)
            payloadPart.CopyTo(buf.AsSpan(dataStart));
        if (_opt.PadFrames)
        {
            for (int i = dataStart + payloadPart.Length; i < _dlc; i++) buf[i] = _opt.PadByte;
            return new FrameScope(_opt.UseFd
                       ? new CanFdFrame(_opt.TxId, buf, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                       : new CanClassicFrame(_opt.TxId, buf, _opt.IsExtendedId),
                       buf);
        }
        else
        {
            int used = dataStart + payloadPart.Length;
            var mem = new ReadOnlyMemory<byte>(buf, 0, used);
            return new FrameScope(_opt.UseFd
                    ? new CanFdFrame(_opt.TxId, mem, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                    : new CanClassicFrame(_opt.TxId, mem, _opt.IsExtendedId),
                     buf);
        }
    }

    private FrameScope BuildFlowControl(IsoFlowStatus status, byte blockSize, byte stmin)
    {
        var (buf, idx0) = RentBase();
        int idx = idx0;
        byte fs = status switch
        {
            IsoFlowStatus.ContinueToSend => 0x0,
            IsoFlowStatus.Wait => 0x1,
            IsoFlowStatus.Overflow => 0x2,
            _ => 0x2
        };
        buf[idx] = (byte)(0x30 | fs);
        buf[idx + 1] = blockSize;
        buf[idx + 2] = stmin;
        if (_opt.PadFrames)
        {
            for (int i = idx + 3; i < _dlc; i++) buf[i] = _opt.PadByte;
            return new FrameScope(_opt.UseFd
                      ? new CanFdFrame(_opt.TxId, buf, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                      : new CanClassicFrame(_opt.TxId, buf, _opt.IsExtendedId),
                      buf);
        }
        else
        {
            int used = idx + 3;
            var mem = new ReadOnlyMemory<byte>(buf, 0, used);
            return new FrameScope(_opt.UseFd
                           ? new CanFdFrame(_opt.TxId, mem, _opt.FdBitRateSwitch, false, _opt.IsExtendedId)
                           : new CanClassicFrame(_opt.TxId, mem, _opt.IsExtendedId),
                            buf);
        }
    }

    private (IsoFlowStatus fs, int bs, TimeSpan stmin) ParseFlowControl(ReadOnlySpan<byte> span)
    {
        int idx = 0;
        if (_opt.ExtendedAddress.HasValue)
        {
            if (span.Length < 2 || span[0] != _opt.ExtendedAddress.Value)
                throw new InvalidOperationException("FC extended address mismatch");
            idx = 1;
        }
        if (span.Length < idx + 3) throw new InvalidOperationException("Invalid FC length");
        if ((span[idx] >> 4) != 0x3) throw new InvalidOperationException("Not an FC frame");
        var fs = (IsoFlowStatus)(span[idx] & 0x0F);
        var bs = span[idx + 1];
        var st = span[idx + 2];
        TimeSpan stmin;
        if (st <= 0x7F) stmin = TimeSpan.FromMilliseconds(st);
        else if (st >= 0xF1 && st <= 0xF9)
        {
            stmin = DecodeStmin(st); // 1 tick = 100ns
        }
        else stmin = TimeSpan.Zero;
        return (fs, bs, stmin);
    }

    private async Task<(IsoFlowStatus fs, int bs, TimeSpan stmin)> WaitFlowControlAsync(
        int timeoutMs, CancellationToken ct, int wftmax = 8, int pciBase = 0)
    {
        var deadline = Environment.TickCount + timeoutMs;
        int waits = 0;

        while (true)
        {
            int remain = (int)Math.Max(0, deadline - Environment.TickCount);
            var batch = await _pipe.ReceiveBatchAsync(1, remain, ct).ConfigureAwait(false);
            if (batch.Count == 0) throw new TimeoutException("Timeout waiting for Flow Control (N_Bs).");

            var d = batch[0].CanFrame.Data.Span;

            if (d.Length < pciBase + 3) continue;

            byte pci = d[pciBase];
            if ((pci >> 4) != 0x3) continue; // must be FC

            var fs = (IsoFlowStatus)(pci & 0x0F);
            byte bs = d[pciBase + 1];
            var stmin = DecodeStmin(d[pciBase + 2]);

            if (fs == IsoFlowStatus.Wait)
            {
                if (++waits > wftmax) throw new InvalidOperationException("WFTmax exceeded.");
                continue;
            }

            return (fs, bs, stmin); //only return when CTS/OVFLW
        }
    }

    private static TimeSpan DecodeStmin(byte st)
    {
        if (st <= 0x7F) return TimeSpan.FromMilliseconds(st);   // 0..127 ms
        if (st >= 0xF1 && st <= 0xF9)
        {
            int us = (st - 0xF0) * 100; // 100..900 µs
            return TimeSpan.FromTicks(us * 10); // 1 tick = 100 ns → 100 µs = 1000 ticks
        }
        return TimeSpan.Zero; // treat others as 0 per common practice
    }


    private int TransmitFrame(in ICanFrame frame)
    {
        // Non-blocking immediate submit; driver may do retries per adapter settings
        return _bus.Transmit(frame);
    }
}

internal readonly struct FrameScope : IDisposable
{
    public FrameScope(ICanFrame canFrame, byte[] rentData)
    {
        CanFrame=canFrame;
        this.rentData=rentData;
    }

    public ICanFrame CanFrame { get; }

    public byte[] rentData { get; }
    public void Dispose()
    {
        try
        {
            ArrayPool<byte>.Shared.Return(rentData);
        }
        catch { /*ignored*/ }
    }
}


internal enum IsoFlowStatus : byte
{
    ContinueToSend = 0x0,
    Wait = 0x1,
    Overflow = 0x2,
}
