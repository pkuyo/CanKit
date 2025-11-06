using System.Runtime.CompilerServices;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.PCAN.Native;
using CanKit.Core.Utils;

namespace CanKit.Adapter.PCAN.Transport;

public class PcanIsoTpChannel : IIsoTpChannel, IOwnership
{
    private readonly PcanIsoTpScheduler _scheduler;

    private TaskCompletionSource<IsoTpDatagram>? _receiveTcs;
    private IDisposable? _owner;
    private IBufferAllocator _allocator;
    private AsyncFramePipe<IsoTpDatagram> _asyncRx;

    public IsoTpOptions Options { get; }
    public event EventHandler<IsoTpDatagram>? DatagramReceived;

    internal PcanIsoTpChannel(PcanIsoTpScheduler scheduler, IsoTpOptions options)
    {
        _scheduler = scheduler;
        Options = options;
        _scheduler.AddChannel(this);
        _asyncRx = new AsyncFramePipe<IsoTpDatagram>(_scheduler.AsyncBufferCapacity);
        _allocator = _scheduler.Options.BufferAllocator;
        _scheduler.MsgReceived += OnMsgReceived;
        _scheduler.BackgroundExceptionOccurred += OnBackgroundExceptionOccurred;
    }

    private void OnBackgroundExceptionOccurred(object sender, Exception e)
    {
        _receiveTcs?.SetException(e);
        // no per-send field; send completions are handled by scheduler
    }

    private unsafe bool OnMsgReceived(in PcanIsoTp.PCanTpMsg msg)
    {
        var (id, addr) = Options.Endpoint.GetRxId();
        var result = id == msg.CanInfo.CanId && (addr is null || addr.Value == msg.MsgData.IsoTp.NetAddrInfo.SourceAddr);
        if (result)
        {
            if (msg.MsgData.IsoTp.NetStatus != PcanIsoTp.PCanTpNetStatus.Ok)
            {
                _receiveTcs?.SetException(new Exception()); //TODO: 异常处理
            }
            var owner = _allocator.Rent((int)msg.MsgData.IsoTp.Length);
            fixed (byte* dst = owner.Memory.Span)
            {
                Unsafe.CopyBlockUnaligned(dst, msg.MsgData.IsoTp.Data, msg.MsgData.IsoTp.Length);
            }

            var data = new IsoTpDatagram(owner, Options.Endpoint);
            _receiveTcs?.SetResult(data);
            var span = Volatile.Read(ref DatagramReceived);
            span?.Invoke(this, data);
            _asyncRx.Publish(data);
        }

        return result;
    }

    public async Task<bool> SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default)
        => await _scheduler.TransmitAsync(this, pdu, ct).ConfigureAwait(false);

    public async Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default)
    {
        if (!await SendAsync(request, ct).ConfigureAwait(false))
        {
            throw new Exception("SendAsync failed"); //TODO:异常处理
        }
        // Wait next datagram from pipe
        var list = await _asyncRx.ReceiveBatchAsync(1, -1, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0] : throw new TaskCanceledException();
    }

    public async Task<IReadOnlyList<IsoTpDatagram>> ReceiveAsync(int count, int timeOutMs = 0, CancellationToken ct = default)
    {
        return await _asyncRx.ReceiveBatchAsync(count, timeOutMs, ct);
    }

    public async IAsyncEnumerable<IsoTpDatagram> GetFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _asyncRx.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    public void Dispose()
    {
        _scheduler.RemoveChannel(this);
        _owner?.Dispose();
    }

    public void AttachOwner(IDisposable owner)
    {
        _owner = owner;
    }
}
