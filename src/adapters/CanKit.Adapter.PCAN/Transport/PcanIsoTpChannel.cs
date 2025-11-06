using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Adapter.PCAN.Native;

namespace CanKit.Adapter.PCAN.Transport;

public class PcanIsoTpChannel : IIsoTpChannel, IOwnership
{
    private readonly PcanIsoTpScheduler _scheduler;

    private TaskCompletionSource<PcanIsoTp.PCanTpMsg>? _receiveTcs;
    private TaskCompletionSource<bool>? _sendTcs;
    private IDisposable? _owner;

    public IsoTpOptions Options { get; }
    public event EventHandler<IsoTpDatagram>? DatagramReceived;

    internal PcanIsoTpChannel(PcanIsoTpScheduler scheduler, IsoTpOptions options)
    {
        _scheduler = scheduler;
        Options = options;
        _scheduler.AddChannel(this);
        _scheduler.MsgReceived += OnMsgReceived;
        _scheduler.BackgroundExceptionOccurred += OnBackgroundExceptionOccurred;
    }

    private void OnBackgroundExceptionOccurred(object sender, Exception e)
    {
        _receiveTcs?.SetException(e);
        _sendTcs?.SetException(e);
    }

    private bool OnMsgReceived(in PcanIsoTp.PCanTpMsg msg)
    {
        var (id, addr) = Options.Endpoint.GetRxId();
        var result = id == msg.CanInfo.CanId && (addr is null || addr.Value == msg.MsgData.IsoTp.NetAddrInfo.SourceAddr);
        if (result)
        {
            if (msg.MsgData.IsoTp.NetStatus != PcanIsoTp.PCanTpNetStatus.Ok)
            {
                _receiveTcs?.SetException(new Exception()); //TODO: 异常处理
            }
            _receiveTcs?.SetResult(msg);
        }

        return result;
    }

    public Task<bool> SendAsync(ReadOnlyMemory<byte> pdu, CancellationToken ct = default)
    {
        _sendTcs = new TaskCompletionSource<bool>();
        ct.Register(() => _sendTcs.SetCanceled());
        throw new NotImplementedException();
    }

    public Task<IsoTpDatagram> RequestAsync(ReadOnlyMemory<byte> request, CancellationToken ct = default)
    {
        _receiveTcs = new TaskCompletionSource<PcanIsoTp.PCanTpMsg>();
        ct.Register(() => _receiveTcs.SetCanceled());
        throw new NotImplementedException();
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
