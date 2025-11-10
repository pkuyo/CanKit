using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Adapter.PCAN.Native;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using Microsoft.Win32.SafeHandles;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Transport;

internal class PcanIsoTpScheduler : IIsoTpScheduler
{
    internal delegate bool MsgReceivedHandler(in PcanIsoTp.PCanTpMsg msg);

    public IBusRTOptionsConfigurator Options { get; }
    public BusNativeHandle NativeHandle { get; }

    private readonly PcanChannel _handle;
    private readonly EventWaitHandle _recEvent;

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    private bool _isDisposed;

    private readonly ConcurrentDictionary<int, IIsoTpChannel> _channels = new();
    private readonly ConcurrentDictionary<int, ConcurrentQueue<PendingTx>> _pendingTx = new();

    internal event EventHandler<Exception>? BackgroundExceptionOccurred;

    internal event MsgReceivedHandler? MsgReceived;

    internal int? AsyncBufferCapacity { get; }

    public PcanIsoTpScheduler(IBusOptions options)
    {
        var cfg = new PcanBusRtConfigurator();
        cfg.Init((PcanBusOptions)options);
        Options = cfg;
        _handle = PcanProvider.ParseHandle(Options.ChannelName!);
        NativeHandle = new BusNativeHandle((int)_handle);
        options.Capabilities = PcanProvider.QueryCapabilities(_handle, Options.Features);
        options.Features = options.Capabilities.Features;
        AsyncBufferCapacity = Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : null;
        try
        {
            if (Api.GetValue(_handle, PcanParameter.ChannelCondition, out uint raw) == PcanStatus.OK)
            {
                var cond = (ChannelCondition)raw;
                if ((cond & ChannelCondition.ChannelAvailable) != ChannelCondition.ChannelAvailable)
                    throw new CanBusCreationException("PCAN handle is not available");
            }
            else
            {
                CanKitLogger.LogWarning("PCAN can't get channel condition for handle");
            }
        }
        catch (PcanBasicException)
        {
            throw new CanBusCreationException("PCAN handle is invalid");
        }

        if (Options.ProtocolMode == CanProtocolMode.CanFd)
        {
            var fd = PcanUtils.MapFdBitrate(Options.BitTiming);
            var st = PcanIsoTp.InitializeFd(_handle, fd);
            if (st != PcanIsoTp.PCanTpStatus.Ok)
            {
                throw new CanBusCreationException($"PCAN InitializeFD failed: {st}");
            }

            CanKitLogger.LogInformation("PCAN: InitializeFD succeeded.");
        }
        else if (Options.ProtocolMode == CanProtocolMode.Can20)
        {
            var baud = PcanUtils.MapClassicBaud(Options.BitTiming);
            var st = PcanIsoTp.Initialize(_handle, baud);
            if (st != PcanIsoTp.PCanTpStatus.Ok)
            {
                throw new CanBusCreationException($"PCAN Initialize failed: {st}");
            }

            CanKitLogger.LogInformation("PCAN: Initialize (classic) succeeded.");
        }

        // Setup receive event and start background loop similar to PcanBus
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        }
        else
        {
            _recEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            _recEvent.SafeWaitHandle?.Close();
            uint evHandle = 0;
            var gst = PcanIsoTp.GetValue(_handle, PcanIsoTp.PCanTpParameter.ReceiveEvent, ref evHandle, sizeof(uint));
            if (gst != PcanIsoTp.PCanTpStatus.Ok)
                throw new InvalidOperationException($"PCAN-ISO-TP: Get ReceiveEvent failed: {gst}");
            _recEvent.SafeWaitHandle = new SafeWaitHandle(new IntPtr(evHandle), false);
        }

        StartReceiveLoop();
    }

    public void AddChannel(IIsoTpChannel channel)
    {
        var endpoint = channel.Options.Endpoint;
        if (!_channels.TryAdd(endpoint.TxId, channel))
        {
            throw new Exception(); //TODO:异常处理
        }

        var mapping = new PcanIsoTp.PCanTpMapping
        {
            CanId = (uint)endpoint.TxId,
            CanIdFlowCtrl = (uint)endpoint.RxId
        };
        mapping.NetAddrInfo.SourceAddr = endpoint.SourceAddress ?? 0;
        mapping.NetAddrInfo.TargetAddr = endpoint.TargetAddress ?? 0;
        mapping.NetAddrInfo.Format = endpoint.AddressingFormat switch
        {
            AddressingFormat.Extended => PcanIsoTp.PCanTpIsotpFormat.Extended,
            AddressingFormat.Mixed => PcanIsoTp.PCanTpIsotpFormat.Mixed,
            AddressingFormat.NormalFixed => PcanIsoTp.PCanTpIsotpFormat.FixedNormal,
            AddressingFormat.Normal => PcanIsoTp.PCanTpIsotpFormat.Normal,
            _ => PcanIsoTp.PCanTpIsotpFormat.Unknown
        };
        mapping.NetAddrInfo.MsgType = PcanIsoTp.PCanTpIsotpMsgType.Diagnostic;
        mapping.NetAddrInfo.TargetType = endpoint.TargetType switch
        {
            TargetType.Functional => PcanIsoTp.PCanTpIsotpAddressing.Functional,
            TargetType.Physical => PcanIsoTp.PCanTpIsotpAddressing.Physical,
            _ => PcanIsoTp.PCanTpIsotpAddressing.Unknown
        };
        PcanIsoTp.AddMapping(_handle, ref mapping);

        if (endpoint.TargetType is not TargetType.Functional)
        {
            mapping.NetAddrInfo.TargetAddr = endpoint.SourceAddress ?? 0;
            mapping.NetAddrInfo.SourceAddr = endpoint.TargetAddress ?? 0;
            mapping.CanId = (uint)endpoint.RxId;
            mapping.CanIdFlowCtrl = (uint)endpoint.TxId;
            PcanIsoTp.AddMapping(_handle, ref mapping);
        }
    }


    public void RemoveChannel(IIsoTpChannel channel)
    {
        var endpoint = channel.Options.Endpoint;
        if (!_channels.TryRemove(endpoint.TxId, out _))
            return;
        PcanIsoTp.RemoveMappings(_handle, (uint)endpoint.TxId);
        if (endpoint.TargetType is not TargetType.Functional)
        {
            PcanIsoTp.RemoveMappings(_handle, (uint)endpoint.RxId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        try
        {
            StopReceiveLoop();
            _ = PcanIsoTp.Uninitialize(_handle);
        }
        finally
        {
            _isDisposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed) throw new CanBusDisposedException();
    }

    private void StartReceiveLoop()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var h = (uint)_recEvent.SafeWaitHandle.DangerousGetHandle().ToInt32();
            var st = PcanIsoTp.SetValue(_handle, PcanIsoTp.PCanTpParameter.ReceiveEvent, ref h, sizeof(uint));
            if (st != PcanIsoTp.PCanTpStatus.Ok)
                throw new InvalidOperationException($"PCAN-ISO-TP: Set ReceiveEvent failed: {st}");
        }

        _pollTask = Task.Run(() => PollLoop(token), token);
        CanKitLogger.LogDebug("PCAN-ISO-TP: Poll loop started.");
    }

    private void StopReceiveLoop()
    {
        var cts = Volatile.Read(ref _pollCts);
        try
        {
            cts?.Cancel();
            _pollTask?.Wait(500);
        }
        catch
        {
        }
        finally
        {
            cts?.Dispose();
            CanKitLogger.LogDebug("PCAN-ISO-TP: Poll loop stopped.");
        }
    }

    private void PollLoop(CancellationToken token)
    {
        var handles = new[] { _recEvent, token.WaitHandle };
        try
        {
            while (!token.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(handles);
                if (signaled == 1) break;
                if (signaled != 0) continue;

                DrainReceive();
            }
        }
        catch (Exception ex)
        {
            HandleBackgroundException(ex);
        }
    }

    private unsafe void DrainReceive()
    {
        PcanIsoTp.PCanTpMsg msg = new PcanIsoTp.PCanTpMsg();

        while (true)
        {
            ulong ts = 0;
            try
            {
                var stAlloc = PcanIsoTp.MsgDataAlloc(ref msg, PcanIsoTp.PCanTpMsgType.IsoTp);
                if (stAlloc != PcanIsoTp.PCanTpStatus.Ok)
                {
                    CanKitLogger.LogWarning($"PCAN-ISO-TP: MsgDataAlloc failed: {stAlloc}");
                    return;
                }

                var st = PcanIsoTp.Read(_handle, &msg, &ts, PcanIsoTp.PCanTpMsgType.IsoTp);
                if (st == PcanIsoTp.PCanTpStatus.NoMessage)
                    break;
                if (st != PcanIsoTp.PCanTpStatus.Ok)
                {
                    // Treat transient bus states as non-fatal; break out of drain
                    if ((uint)st >= (uint)PcanIsoTp.PCanTpStatus.BusLight &&
                        (uint)st <= (uint)PcanIsoTp.PCanTpStatus.BusOff)
                        break;
                    throw new InvalidOperationException($"PCAN-ISO-TP Read failed: {st}");
                }

                var snap = Volatile.Read(ref MsgReceived);
                if (msg.Type == PcanIsoTp.PCanTpMsgType.IsoTp)
                {
                    // TX confirmation (loopback)
                    if ((msg.MsgData.IsoTp.Flags & PcanIsoTp.PCanTpMsgFlag.Loopback) == PcanIsoTp.PCanTpMsgFlag.Loopback)
                    {
                        if (_pendingTx.TryGetValue((int)msg.CanInfo.CanId, out var q))
                        {
                            while (q.TryPeek(out var head))
                            {
                                bool equal = PcanIsoTp.MsgEqual(ref head.Compare, ref msg, true);
                                if (!equal) break;

                                q.TryDequeue(out _);
                                try
                                {
                                    if (msg.MsgData.IsoTp.NetStatus == PcanIsoTp.PCanTpNetStatus.Ok)
                                        head.Tcs.TrySetResult(true);
                                    else
                                        head.Tcs.TrySetException(new Exception()); //TODO：异常处理
                                }
                                finally
                                {
                                    _ = PcanIsoTp.MsgDataFree(ref head.Compare);
                                }
                                break;
                            }
                        }
                        continue; // don't route loopback to channels
                    }

                    if (snap != null)
                    {
                        foreach (var del in snap.GetInvocationList())
                        {
                            var f = (MsgReceivedHandler)del;
                            if (f(msg))
                                break;
                        }
                    }
                }
            }
            finally
            {
                _ = PcanIsoTp.MsgDataFree(ref msg);
            }
        }
    }

    internal async Task<bool> TransmitAsync(IIsoTpChannel channel, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var ep = channel.Options.Endpoint;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

        var q = _pendingTx.GetOrAdd(ep.TxId, _ => new ConcurrentQueue<PendingTx>());

        var task = tcs.Task;
        unsafe
        {
            PcanIsoTp.PCanTpMsg msg = new PcanIsoTp.PCanTpMsg();
            PcanIsoTp.PCanTpMsg cmp = new PcanIsoTp.PCanTpMsg();
            try
            {
                var st = PcanIsoTp.MsgDataAlloc(ref msg, PcanIsoTp.PCanTpMsgType.IsoTp);
                if (st != PcanIsoTp.PCanTpStatus.Ok)
                    throw new InvalidOperationException($"MsgDataAlloc failed: {st}");

                st = PcanIsoTp.MsgDataAlloc(ref cmp, PcanIsoTp.PCanTpMsgType.IsoTp);
                if (st != PcanIsoTp.PCanTpStatus.Ok)
                    throw new InvalidOperationException($"MsgDataAlloc(compare) failed: {st}");

                var nai = stackalloc PcanIsoTp.PCanTpNetAddrInfo[1];
                nai->MsgType = PcanIsoTp.PCanTpIsotpMsgType.Diagnostic;
                nai->Format = ep.AddressingFormat switch
                {
                    AddressingFormat.Extended => PcanIsoTp.PCanTpIsotpFormat.Extended,
                    AddressingFormat.Mixed => PcanIsoTp.PCanTpIsotpFormat.Mixed,
                    AddressingFormat.NormalFixed => PcanIsoTp.PCanTpIsotpFormat.FixedNormal,
                    _ => PcanIsoTp.PCanTpIsotpFormat.Normal
                };
                nai->TargetType = ep.TargetType == TargetType.Functional
                    ? PcanIsoTp.PCanTpIsotpAddressing.Functional
                    : PcanIsoTp.PCanTpIsotpAddressing.Physical;
                nai->SourceAddr = (ushort)(ep.SourceAddress ?? 0);
                nai->TargetAddr = (ushort)(ep.TargetAddress ?? 0);
                nai->ExtensionAddr = ep.ExtendedAddress ?? (byte)0;

                MessageType canType = ep.IsExtendedId ? MessageType.Extended : MessageType.Standard;
                if (Options.ProtocolMode == CanProtocolMode.CanFd)
                    canType |= MessageType.FlexibleDataRate;

                fixed (byte* p = data.Span)
                {
                    st = PcanIsoTp.MsgDataInit(ref msg, 0xFFFFFFFFu, canType, (uint)data.Length, (IntPtr)p, new IntPtr(nai));
                    if (st != PcanIsoTp.PCanTpStatus.Ok)
                        throw new InvalidOperationException($"MsgDataInit failed: {st}");
                }

                var cst = PcanIsoTp.MsgCopy(ref cmp, ref msg);
                if (cst != PcanIsoTp.PCanTpStatus.Ok)
                    throw new InvalidOperationException($"MsgCopy failed: {cst}");

                q.Enqueue(new PendingTx(cmp, tcs));

                var wst = PcanIsoTp.Write(_handle, ref msg);
                if (wst != PcanIsoTp.PCanTpStatus.Ok)
                {
                    if (q.TryDequeue(out var head))
                    {
                        _ = PcanIsoTp.MsgDataFree(ref head.Compare);
                    }
                    throw new InvalidOperationException($"PCAN-ISO-TP Write failed: {wst}");
                }

                // fallthrough to await outside unsafe block
            }
            finally
            {
                _ = PcanIsoTp.MsgDataFree(ref msg);
            }
        }
        return await task.ConfigureAwait(false);
    }

    private void HandleBackgroundException(Exception ex)
    {
        try
        {
            CanKitLogger.LogError("PCAN-ISO-TP bus occured background exception.", ex);
        }
        catch
        {
        }

        try
        {
            var snap = Volatile.Read(ref BackgroundExceptionOccurred);
            snap?.Invoke(this, ex);
        }
        catch
        {
        }
    }
}

internal sealed class PendingTx
{
    public PcanIsoTp.PCanTpMsg Compare;
    public TaskCompletionSource<bool> Tcs;
    public PendingTx(PcanIsoTp.PCanTpMsg compare, TaskCompletionSource<bool> tcs)
    {
        Compare = compare; Tcs = tcs;
    }
}
