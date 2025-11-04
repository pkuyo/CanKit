using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Adapter.PCAN.Native;
using CanKit.Core.Diagnostics;
using CanKit.Core.Exceptions;
using Microsoft.Win32.SafeHandles;
using Peak.Can.Basic;

namespace CanKit.Adapter.PCAN.Transport;

public class PcanIsoTpBus : IDisposable
{
    public PcanIsoTpBus(IBusInitOptionsConfigurator cfg, IIsoTpOptions options)
    {
        Options = cfg;
        _handle = PcanProvider.ParseHandle(Options.ChannelName!);
        NativeHandle = new BusNativeHandle((int)_handle);
        options.Capabilities = PcanProvider.QueryCapabilities(_handle, Options.Features);
        options.Features = options.Capabilities.Features;
        _asyncBufferCapacity = Options.AsyncBufferCapacity > 0 ? Options.AsyncBufferCapacity : (int?)null;
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

    public IBusInitOptionsConfigurator Options { get; }
    public BusNativeHandle NativeHandle { get; }

    private PcanChannel _handle;
    private readonly int? _asyncBufferCapacity;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private readonly EventWaitHandle _recEvent;
    private bool _isDisposed;

    public event EventHandler<Exception>? BackgroundExceptionOccurred;

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
        catch { }
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
                    if ((uint)st >= (uint)PcanIsoTp.PCanTpStatus.BusLight && (uint)st <= (uint)PcanIsoTp.PCanTpStatus.BusOff)
                        break;
                    throw new InvalidOperationException($"PCAN-ISO-TP Read failed: {st}");
                }
            }
            finally
            {
                _ = PcanIsoTp.MsgDataFree(ref msg);
            }



        }

    }

    private void HandleBackgroundException(Exception ex)
    {
        try { CanKitLogger.LogError("PCAN-ISO-TP bus occured background exception.", ex); } catch { }
        try { var snap = Volatile.Read(ref BackgroundExceptionOccurred); snap?.Invoke(this, ex); } catch { }
    }
}
