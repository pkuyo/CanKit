using System.Buffers;
using System.Diagnostics;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.API.Transport.Excpetions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Utils;

namespace CanKit.Transport.IsoTp.Core;

#region State

internal enum TxState
{
    Idle,
    SendingSF,      // 当前有一个 SF 要发 / 已发待 echo
    SendingFF,       // 当前有一个 FF 要发 / 已发待 echo
    WaitingFC,      // FF 已完成，等待对端 FC
    SendingCF,// 正在发送 CF 序列
    Failed
}

internal enum RxState
{
    Idle,
    Receiving,
    Failed
}

internal enum TimerKind
{
    N_As,
    N_Bs,
    N_Cs,
    N_Ar,
    N_Cr,
}

#endregion

#region Event

internal abstract record IsoTpEvent
{
    public sealed record SendRequested(TxOperation Operation) : IsoTpEvent;

    public sealed record RemoteSingleFrame(CanReceiveData Rx, Pci Pci) : IsoTpEvent;

    public sealed record RemoteFirstFrame(CanReceiveData Rx, Pci Pci) : IsoTpEvent;

    public sealed record RemoteConsecutiveFrame(CanReceiveData Rx, Pci Pci) : IsoTpEvent;

    public sealed record RemoteFlowControl(Pci Pci) : IsoTpEvent;

    public sealed record EchoReceived(PciType Type) : IsoTpEvent;

    public sealed record OutboundSendFailed(OutboundItem Item, Exception Error) : IsoTpEvent;

    public sealed record TimerExpired(TimerKind Kind) : IsoTpEvent;
}

#endregion

#region Action

internal abstract record IsoTpAction
{

    public sealed record NotifyWorkAvailable : IsoTpAction;

    public sealed record QueueOutbound(OutboundItem Item) : IsoTpAction;

    public sealed record CompleteSend(TxOperation Operation) : IsoTpAction;

    public sealed record FailSend(TxOperation? Operation, Exception Error) : IsoTpAction;

    public sealed record EmitDatagram(IMemoryOwner<byte> Owner) : IsoTpAction;

    public sealed record AbortReceive : IsoTpAction;
}

#endregion

#region Context

internal sealed class TxContext
{
    public TxState State { get; set; } = TxState.Idle;

    public TxOperation? Current { get; set; }

    /// <summary>当前是否已经把一帧交给下层发送，正在等 echo</summary>
    public bool AwaitingEcho { get; set; }

    /// <summary>原始 payload 已经推进到的位置（按业务 payload 计，不含 PCI）</summary>
    public int Offset { get; set; }

    /// <summary>下一帧 CF 的 SN</summary>
    public byte NextSn { get; set; } = 1;

    /// <summary>当前 block 还允许发送多少个 CF。int.MaxValue 表示无限。</summary>
    public int BlockRemaining { get; set; }

    public TimeSpan Stmin { get; set; } = TimeSpan.Zero;

    /// <summary>最近一次成功送出 CF 的时间戳，用于 STmin</summary>
    public long LastCfTicks { get; set; }

    public void Reset()
    {
        State = TxState.Idle;
        Current = null;
        AwaitingEcho = false;
        Offset = 0;
        NextSn = 1;
        BlockRemaining = 0;
        Stmin = TimeSpan.Zero;
        LastCfTicks = 0;
    }
}

internal sealed class RxContext
{
    public RxState State { get; set; } = RxState.Idle;

    public IMemoryOwner<byte>? Owner { get; set; }

    public int ExpectedLength { get; set; }

    public int ReceivedBytes { get; set; }

    public byte NextSn { get; set; }

    /// <summary>当前 block 还可接收多少个 CF。int.MaxValue 表示无限。</summary>
    public int BlockRemaining { get; set; }

    /// <summary>是否需要给对端发 FC(CTS)</summary>
    public bool FcPending { get; set; }

    /// <summary>FC 已经准备发出/已交给下层，正在等 echo</summary>
    public bool FcAwaitingEcho { get; set; }

    public void Reset()
    {
        Owner?.Dispose();
        Owner = null;
        State = RxState.Idle;
        ExpectedLength = 0;
        ReceivedBytes = 0;
        NextSn = 0;
        BlockRemaining = 0;
        FcPending = false;
        FcAwaitingEcho = false;
    }
}

#endregion

#region Operation / Outbound

internal sealed class TxOperation : IDisposable
{
    private readonly IMemoryOwner<byte> _payloadOwner;
    private int _disposed;

    public int TxAddress { get; }
    public int? ExtendAddress { get; }

    public int Length { get; }

    public bool Padding { get; }

    public bool CanFd { get; }

    public ReadOnlyMemory<byte> Payload => _payloadOwner.Memory;

    public TaskCompletionSource<bool> Tcs { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationTokenRegistration CancellationRegistration { get; private set; }

    public DateTime StartedAt { get; } = DateTime.Now;

    public bool IsCanceled { get; private set; }

    private TxOperation(
        int txAddress,
        int? extendAddress,
        IMemoryOwner<byte> payloadOwner,
        int length,
        bool padding,
        bool canFd)
    {
        TxAddress = txAddress;
        ExtendAddress = extendAddress;
        _payloadOwner = payloadOwner;
        Length = length;
        Padding = padding;
        CanFd = canFd;
    }

    public static TxOperation Create(
        IsoTpEndpoint endpoint,
        IBufferAllocator allocator,
        ReadOnlySpan<byte> payload,
        bool padding,
        bool canFd)
    {
        var owner = allocator.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new TxOperation(endpoint.TxId, endpoint.TargetAddress, owner, payload.Length, padding, canFd);
    }

    public void BindCancellation(Action callback, CancellationToken token)
    {
        if (!token.CanBeCanceled) return;
        CancellationRegistration = token.Register(callback, useSynchronizationContext: false);
    }

    public void MarkCanceled()
    {
        IsCanceled = true;
        Tcs.TrySetCanceled();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CancellationRegistration.Dispose();
        _payloadOwner.Dispose();
    }
}

internal readonly record struct OutboundItem(
    TxOperation? Operation,
    IsoTpEndpoint Endpoint,
    CanFrame Frame,
    PciType Type,
    bool IsControlFrame);

#endregion

internal sealed class IsoTpChannelCore : IDisposable
{
    private readonly object _gate = new();

    private readonly Queue<TxOperation> _txPending = new();
    private readonly Queue<OutboundItem> _readyControlFrames = new();
    private readonly Queue<OutboundItem> _readyDataFrames = new();

    private readonly TxContext _tx = new();
    private readonly RxContext _rx = new();

    private readonly Deadline _nAs;
    private readonly Deadline _nBs;
    private readonly Deadline _nCs;
    private readonly Deadline _nAr;
    private readonly Deadline _nCr;

    private readonly IBufferAllocator _allocator;

    internal event EventHandler<IsoTpDatagram>? DatagramReceived;

    /// <summary>
    /// 存在可发送帧。
    /// </summary>
    internal event Action? OnWorkAvailable;

    public IsoTpEndpoint Endpoint { get; }
    public IsoTpOptions Options { get; }
    public RxFcPolicy FcPolicy { get; }

    public IsoTpChannelCore(
        IsoTpEndpoint endpoint,
        IsoTpOptions options,
        RxFcPolicy fcPolicy,
        IBufferAllocator allocator)
    {
        Endpoint = endpoint;
        Options = options;
        FcPolicy = fcPolicy;
        _allocator = allocator;

        _nAs = new Deadline(options.N_As);
        _nBs = new Deadline(options.N_Bs);
        _nCs = new Deadline(options.N_Cs);
        _nAr = new Deadline(options.N_Ar);
        _nCr = new Deadline(options.N_Cr);
    }

    #region Public API

    public bool Match(in CanReceiveData rx)
    {
        return rx.CanFrame.IsExtendedFrame == Endpoint.IsExtendedId &&
               rx.CanFrame.ID == Endpoint.RxId;
    }

    public bool Match(in IsoTpEndpoint tx)
    {
        return tx.TxId == Endpoint.TxId &&
               tx.TargetAddress == Endpoint.TargetAddress;
    }

    public Task<bool> SendAsync(ReadOnlySpan<byte> payload, bool padding, bool canFd, CancellationToken ct)
    {
        lock (_gate)
        {
            var op = TxOperation.Create(Endpoint, _allocator, payload, padding, canFd);
            op.BindCancellation(() => CancelOperation(op), ct);

            _txPending.Enqueue(op);

            Execute([new IsoTpAction.NotifyWorkAvailable()]);

            return op.Tcs.Task;
        }
    }

    public void OnRx(in CanReceiveData rx)
    {
        lock (_gate)
        {
            if (!FrameCodec.TryParsePci(rx, Endpoint, out var pci))
            {
                //TODO:解析失败
                return;
            }

            List<IsoTpAction> actions = new(4);

            if (rx.IsEcho)
            {
                Reduce(new IsoTpEvent.EchoReceived(pci.Type), actions);
            }
            else
            {
                switch (pci.Type)
                {
                    case PciType.SF:
                        Reduce(new IsoTpEvent.RemoteSingleFrame(rx, pci), actions);
                        break;

                    case PciType.FF:
                        Reduce(new IsoTpEvent.RemoteFirstFrame(rx, pci), actions);
                        break;

                    case PciType.CF:
                        Reduce(new IsoTpEvent.RemoteConsecutiveFrame(rx, pci), actions);
                        break;

                    case PciType.FC:
                        Reduce(new IsoTpEvent.RemoteFlowControl(pci), actions);
                        break;
                }
            }

            Execute(actions);
        }
    }

    /// <summary>
    /// 外层发送器调用：取帧
    /// </summary>
    public bool TryDequeueReadyFrame(long nowTicks, TimeSpan? globalGuard, out OutboundItem item, out TimeSpan waitTime)
    {
        lock (_gate)
        {
            List<IsoTpAction> actions = new(2);
            PlanOutbound(nowTicks, globalGuard ?? TimeSpan.Zero, actions);
            Execute(actions);

            if (_readyControlFrames.Count > 0)
            {
                item = _readyControlFrames.Dequeue();
                waitTime = TimeSpan.Zero;
                return true;
            }

            if (_readyDataFrames.Count > 0)
            {
                item = _readyDataFrames.Dequeue();
                waitTime = TimeSpan.Zero;
                return true;
            }

            item = default;
            waitTime = ComputeWaitTime(nowTicks, globalGuard ?? TimeSpan.Zero);
            return false;
        }
    }


    public void OnFrameAccepted(in OutboundItem item)
    {
        lock (_gate)
        {
            if (item.Type == PciType.CF && item.Operation is not null && _tx.Current == item.Operation)
            {
                _tx.LastCfTicks = Stopwatch.GetTimestamp();
            }

            item.Frame.Dispose();
        }
    }

    public void OnFrameSendFailed(in OutboundItem item, Exception error)
    {
        lock (_gate)
        {
            // 发送失败时，frame 内存应释放
            item.Frame.Dispose();

            List<IsoTpAction> actions = new(2);
            Reduce(new IsoTpEvent.OutboundSendFailed(item, error), actions);
            Execute(actions);
        }
    }

    public TimeSpan GetNextExpiryTime(long nowTicks, TimeSpan? globalGuard)
    {
        lock (_gate)
        {
            var min = TimeSpan.MaxValue;

            // TX 相关
            switch (_tx.State)
            {
                case TxState.SendingSF:
                case TxState.SendingFF:
                    min = Min(min, _nAs.Remaining);
                    break;

                case TxState.WaitingFC:
                    min = Min(min, _nBs.Remaining);
                    break;

                case TxState.SendingCF:
                    min = Min(min, _tx.AwaitingEcho ? _nAs.Remaining : _nCs.Remaining);
                    break;
            }

            // RX 相关
            switch (_rx.State)
            {
                case RxState.Receiving:
                    if (_rx.FcPending || _rx.FcAwaitingEcho)
                        min = Min(min, _nAr.Remaining);
                    else
                        min = Min(min, _nCr.Remaining);
                    break;
            }

            // STmin / 全局 guard
            var sendWait = ComputeWaitTime(nowTicks, globalGuard ?? TimeSpan.Zero);
            min = Min(min, sendWait);

            return min;
        }

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a <= b ? a : b;
    }

    /// <summary>
    /// 由外层定时器驱动，检查哪个 deadline 到期了。
    /// </summary>
    public void CheckTimeouts()
    {
        lock (_gate)
        {
            List<IsoTpAction> actions = new(2);

            if (_tx.State == TxState.SendingSF && _nAs.Remaining <= TimeSpan.Zero)
                Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_As), actions);

            if (_tx.State == TxState.SendingFF && _nAs.Remaining <= TimeSpan.Zero)
                Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_As), actions);

            if (_tx.State == TxState.WaitingFC && _nBs.Remaining <= TimeSpan.Zero)
                Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_Bs), actions);

            if (_tx.State == TxState.SendingCF)
            {
                if (_tx.AwaitingEcho && _nAs.Remaining <= TimeSpan.Zero)
                    Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_As), actions);
                else if (!_tx.AwaitingEcho && _nCs.Remaining <= TimeSpan.Zero)
                    Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_Cs), actions);
            }

            if (_rx.State == RxState.Receiving)
            {
                if ((_rx.FcPending || _rx.FcAwaitingEcho) && _nAr.Remaining <= TimeSpan.Zero)
                    Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_Ar), actions);
                else if (!_rx.FcPending && !_rx.FcAwaitingEcho && _nCr.Remaining <= TimeSpan.Zero)
                    Reduce(new IsoTpEvent.TimerExpired(TimerKind.N_Cr), actions);
            }

            Execute(actions);
        }
    }

    #endregion

    #region Reducer

    private void Reduce(IsoTpEvent ev, List<IsoTpAction> actions)
    {
        switch (ev)
        {
            case IsoTpEvent.SendRequested e:
                OnSendRequested(e.Operation, actions);
                break;

            case IsoTpEvent.RemoteSingleFrame e:
                OnRemoteSingleFrame(e.Rx, e.Pci, actions);
                break;

            case IsoTpEvent.RemoteFirstFrame e:
                OnRemoteFirstFrame(e.Rx, e.Pci, actions);
                break;

            case IsoTpEvent.RemoteConsecutiveFrame e:
                OnRemoteConsecutiveFrame(e.Rx, e.Pci, actions);
                break;

            case IsoTpEvent.RemoteFlowControl e:
                OnRemoteFlowControl(e.Pci, actions);
                break;

            case IsoTpEvent.EchoReceived e:
                OnEchoReceived(e.Type, actions);
                break;

            case IsoTpEvent.OutboundSendFailed e:
                OnOutboundSendFailed(e.Item, e.Error, actions);
                break;

            case IsoTpEvent.TimerExpired e:
                OnTimerExpired(e.Kind, actions);
                break;
        }
    }

    private void OnSendRequested(TxOperation operation, List<IsoTpAction> actions)
    {
        _txPending.Enqueue(operation);
        actions.Add(new IsoTpAction.NotifyWorkAvailable());
    }

    private void OnRemoteSingleFrame(in CanReceiveData rx, Pci pci, List<IsoTpAction> actions)
    {
        AbortReceiveCore();

        var payload = GetFramePayload(rx.CanFrame, Endpoint, pci);
        var length = Math.Min(payload.Length, pci.Len);
        if (length <= 0) return;

        var owner = _allocator.Rent(length);
        payload.Slice(0, length).CopyTo(owner.Memory.Span);
        actions.Add(new IsoTpAction.EmitDatagram(owner));
    }

    private void OnRemoteFirstFrame(in CanReceiveData rx, Pci pci, List<IsoTpAction> actions)
    {
        AbortReceiveCore();

        var total = pci.Len;
        if (total <= 0)
            return;

        var payload = GetFramePayload(rx.CanFrame, Endpoint, pci);
        var copied = Math.Min(payload.Length, total);

        var owner = _allocator.Rent(total);
        if (copied > 0)
        {
            payload.Slice(0, copied).CopyTo(owner.Memory.Span);
        }

        _rx.Owner = owner;
        _rx.State = RxState.Receiving;
        _rx.ExpectedLength = total;
        _rx.ReceivedBytes = copied;
        _rx.NextSn = 1;

        QueueFlowControlCts();
        _nAr.Restart();

        actions.Add(new IsoTpAction.NotifyWorkAvailable());

        if (_rx.ReceivedBytes >= _rx.ExpectedLength)
        {
            CompleteReceive(actions);
        }
    }

    private void OnRemoteConsecutiveFrame(in CanReceiveData rx, Pci pci, List<IsoTpAction> actions)
    {
        if (_rx.State != RxState.Receiving || _rx.Owner is null)
        {
            AbortReceiveCore();
            return;
        }

        // 如果此时本端正准备发 FC 或 FC 还没 echo，
        // 对端继续发 CF，视为时序错误，直接中止本次接收
        if (_rx.FcPending || _rx.FcAwaitingEcho)
        {
            AbortReceiveCore();
            return;
        }

        if (pci.SN != _rx.NextSn)
        {
            AbortReceiveCore();
            return;
        }

        var payload = GetFramePayload(rx.CanFrame, Endpoint, pci);
        var remaining = _rx.ExpectedLength - _rx.ReceivedBytes;
        var take = Math.Min(payload.Length, remaining);

        if (!Options.RxPadding && payload.Length > take)
        {
            //TODO:异常处理
            AbortReceiveCore();
            return;
        }

        if (take > 0)
        {
            payload.Slice(0, take).CopyTo(_rx.Owner.Memory.Span.Slice(_rx.ReceivedBytes));
            _rx.ReceivedBytes += take;
        }

        if (_rx.ReceivedBytes >= _rx.ExpectedLength)
        {
            CompleteReceive(actions);
            return;
        }

        _rx.NextSn = (byte)((_rx.NextSn + 1) % IsoTpConst.SN_Mod);

        if (_rx.BlockRemaining != int.MaxValue)
        {
            _rx.BlockRemaining--;
            if (_rx.BlockRemaining <= 0)
            {
                QueueFlowControlCts();
                _nAr.Restart();
                actions.Add(new IsoTpAction.NotifyWorkAvailable());
                return;
            }
        }

        _nCr.Restart();
    }

    private void OnRemoteFlowControl(Pci pci, List<IsoTpAction> actions)
    {
        if (_tx.State != TxState.WaitingFC || _tx.Current is null)
        {
            // TODO:非法时机收到 FC
            return;
        }

        switch (pci.FS)
        {
            case FlowStatus.CTS:
                _tx.Stmin = pci.STmin;
                _tx.BlockRemaining = pci.BS <= 0 ? int.MaxValue : pci.BS;
                _tx.State = TxState.SendingCF;
                _tx.AwaitingEcho = false;
                _nCs.Restart();
                actions.Add(new IsoTpAction.NotifyWorkAvailable());
                break;

            case FlowStatus.WT:
                _nBs.Restart();
                break;

            case FlowStatus.OVFLW:
                FailCurrentSend(actions, new IsoTpException(
                    IsoTpErrorCode.Remote_Overflow,
                    "Peer reported FC.OVFLW",
                    Endpoint));
                break;
        }
    }

    private void OnEchoReceived(PciType type, List<IsoTpAction> actions)
    {
        switch (type)
        {
            case PciType.SF:
                if (_tx.State == TxState.SendingSF && _tx.Current is not null && _tx.AwaitingEcho)
                {
                    var op = _tx.Current;
                    _tx.Reset();
                    actions.Add(new IsoTpAction.CompleteSend(op));
                    actions.Add(new IsoTpAction.NotifyWorkAvailable());
                }
                break;

            case PciType.FF:
                if (_tx.State == TxState.SendingFF && _tx.Current is not null && _tx.AwaitingEcho)
                {
                    _tx.AwaitingEcho = false;
                    _tx.State = TxState.WaitingFC;
                    _nBs.Restart();
                }
                break;

            case PciType.CF:
                if (_tx.State == TxState.SendingCF && _tx.Current is not null && _tx.AwaitingEcho)
                {
                    _tx.AwaitingEcho = false;

                    if (_tx.Offset >= _tx.Current.Length)
                    {
                        var op = _tx.Current;
                        _tx.Reset();
                        actions.Add(new IsoTpAction.CompleteSend(op));
                        actions.Add(new IsoTpAction.NotifyWorkAvailable());
                        return;
                    }

                    if (_tx.BlockRemaining == 0)
                    {
                        _tx.State = TxState.WaitingFC;
                        _nBs.Restart();
                        return;
                    }

                    _nCs.Restart();
                    actions.Add(new IsoTpAction.NotifyWorkAvailable());
                }
                break;

            case PciType.FC:
                if (_rx.State == RxState.Receiving && _rx.FcAwaitingEcho)
                {
                    _rx.FcPending = false;
                    _rx.FcAwaitingEcho = false;
                    _nCr.Restart();
                }
                break;
        }
    }

    private void OnOutboundSendFailed(in OutboundItem item, Exception error, List<IsoTpAction> actions)
    {
        if (item.IsControlFrame)
        {
            AbortReceiveCore();
            return;
        }

        if (item.Operation is not null && _tx.Current == item.Operation)
        {
            FailCurrentSend(actions, error);
        }
    }

    private void OnTimerExpired(TimerKind kind, List<IsoTpAction> actions)
    {
        switch (kind)
        {
            case TimerKind.N_As:
                if (_tx.Current is not null)
                {
                    FailCurrentSend(actions, new TimeoutException("N_As timeout"));
                }
                break;

            case TimerKind.N_Bs:
                if (_tx.Current is not null && _tx.State == TxState.WaitingFC)
                {
                    FailCurrentSend(actions, new TimeoutException("N_Bs timeout"));
                }
                break;

            case TimerKind.N_Cs:
                if (_tx.Current is not null && _tx.State == TxState.SendingCF && !_tx.AwaitingEcho)
                {
                    FailCurrentSend(actions, new TimeoutException("N_Cs timeout"));
                }
                break;

            case TimerKind.N_Ar:
                AbortReceiveCore();
                break;

            case TimerKind.N_Cr:
                AbortReceiveCore();
                break;
        }
    }

    #endregion

    #region Planner

    /// <summary>
    /// 当前如果允许发送，应该生成哪一帧
    /// </summary>
    private void PlanOutbound(long nowTicks, TimeSpan globalGuard, List<IsoTpAction> actions)
    {
        if (_readyControlFrames.Count > 0 || _readyDataFrames.Count > 0)
            return;
        if (_rx is { State: RxState.Receiving, FcPending: true, FcAwaitingEcho: false })
        {
            var frame = FrameCodec.BuildFC(
                Endpoint,
                _allocator,
                FlowStatus.CTS,
                (byte)Math.Max(FcPolicy.BS, 0),
                FrameCodec.EncodeStmin(FcPolicy.STmin),
                padding: true,
                canfd: false);

            _rx.FcAwaitingEcho = true;

            actions.Add(new IsoTpAction.QueueOutbound(new OutboundItem(
                Operation: null,
                Endpoint: Endpoint,
                Frame: frame,
                Type: PciType.FC,
                IsControlFrame: true)));

            return;
        }

        EnsureActiveTx(actions);

        if (_tx.Current is null)
            return;

        if (_tx.AwaitingEcho)
            return;

        switch (_tx.State)
        {
            case TxState.SendingSF:
            {
                var frame = FrameCodec.BuildSF(
                    Endpoint,
                    _allocator,
                    _tx.Current.Payload.Span,
                    _tx.Current.Padding,
                    _tx.Current.CanFd);

                _tx.AwaitingEcho = true;
                _nAs.Restart();

                actions.Add(new IsoTpAction.QueueOutbound(new OutboundItem(
                    _tx.Current,
                    Endpoint: Endpoint,
                    frame,
                    PciType.SF,
                    IsControlFrame: false)));

                return;
            }

            case TxState.SendingFF:
            {
                var frame = FrameCodec.BuildFF(
                    Endpoint,
                    _allocator,
                    _tx.Current.Length,
                    _tx.Current.Payload.Span,
                    _tx.Current.CanFd);

                _tx.AwaitingEcho = true;
                _nAs.Restart();

                actions.Add(new IsoTpAction.QueueOutbound(new OutboundItem(
                    _tx.Current,
                    Endpoint: Endpoint,
                    frame,
                    PciType.FF,
                    IsControlFrame: false)));

                return;
            }

            case TxState.SendingCF:
            {
                if (_tx.BlockRemaining == 0)
                {
                    _tx.State = TxState.WaitingFC;
                    _nBs.Restart();
                    return;
                }

                var wait = ComputeCfWait(nowTicks, globalGuard);
                if (wait > TimeSpan.Zero)
                    return;

                var cfMax = CalcCfPayloadMax(_tx.Current.CanFd, Endpoint.AddrUsePayload);
                var remaining = _tx.Current.Length - _tx.Offset;
                var take = Math.Min(cfMax, remaining);

                if (take <= 0)
                    return;

                var frame = FrameCodec.BuildCF(
                    Endpoint,
                    _allocator,
                    _tx.NextSn,
                    _tx.Current.Payload.Span.Slice(_tx.Offset, take),
                    _tx.Current.Padding,
                    _tx.Current.CanFd);

                _tx.Offset += take;
                _tx.NextSn = (byte)((_tx.NextSn + 1) % IsoTpConst.SN_Mod);

                if (_tx.BlockRemaining != int.MaxValue)
                    _tx.BlockRemaining--;

                _tx.AwaitingEcho = true;
                _nAs.Restart();

                actions.Add(new IsoTpAction.QueueOutbound(new OutboundItem(
                    _tx.Current,
                    Endpoint: Endpoint,
                    frame,
                    PciType.CF,
                    IsControlFrame: false)));

                return;
            }
        }
    }

    private void EnsureActiveTx(List<IsoTpAction> actions)
    {
        if (_tx.Current is not null || _tx.State != TxState.Idle)
            return;

        while (_txPending.Count > 0)
        {
            var op = _txPending.Dequeue();

            if (op.IsCanceled)
            {
                op.Dispose();
                continue;
            }

            _tx.Current = op;
            _tx.AwaitingEcho = false;
            _tx.NextSn = 1;
            _tx.LastCfTicks = 0;
            _tx.Stmin = TimeSpan.Zero;

            var sfMax = CalcSfPayloadMax(op.CanFd, Endpoint.AddrUsePayload);
            if (op.Length <= sfMax)
            {
                _tx.Offset = op.Length;
                _tx.BlockRemaining = 0;
                _tx.State = TxState.SendingSF;
            }
            else
            {
                var ffPayload = CalcFfPayloadMax(op.CanFd, Endpoint.AddrUsePayload, op.Length);
                _tx.Offset = Math.Min(ffPayload, op.Length);
                _tx.BlockRemaining = 0;
                _tx.State = TxState.SendingFF;
            }

            actions.Add(new IsoTpAction.NotifyWorkAvailable());
            return;
        }
    }

    #endregion

    #region Executor

    private void Execute(IEnumerable<IsoTpAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case IsoTpAction.NotifyWorkAvailable:
                    OnWorkAvailable?.Invoke();
                    break;

                case IsoTpAction.QueueOutbound e:
                    if (e.Item.IsControlFrame)
                        _readyControlFrames.Enqueue(e.Item);
                    else
                        _readyDataFrames.Enqueue(e.Item);
                    break;

                case IsoTpAction.CompleteSend e:
                    e.Operation.Tcs.TrySetResult(true);
                    e.Operation.Dispose();
                    break;

                case IsoTpAction.FailSend e:
                    if (e.Operation is not null)
                    {
                        e.Operation.Tcs.TrySetException(e.Error);
                        e.Operation.Dispose();
                    }
                    break;

                case IsoTpAction.EmitDatagram e:
                    EmitDatagram(e.Owner);
                    break;

                case IsoTpAction.AbortReceive:
                    AbortReceiveCore();
                    break;
            }
        }
    }

    #endregion

    #region Helpers

    private void CancelOperation(TxOperation op)
    {
        lock (_gate)
        {
            if (op.IsCanceled) return;

            op.MarkCanceled();

            if (_tx.Current == op)
            {
                List<IsoTpAction> actions = new(2);
                FailCurrentSend(actions, new OperationCanceledException("User canceled send"));
                Execute(actions);
            }
        }
    }

    private void FailCurrentSend(List<IsoTpAction> actions, Exception error)
    {
        var op = _tx.Current;
        _tx.Reset();

        if (op is not null)
            actions.Add(new IsoTpAction.FailSend(op, error));

        actions.Add(new IsoTpAction.NotifyWorkAvailable());
    }

    private void QueueFlowControlCts()
    {
        _rx.FcPending = true;
        _rx.FcAwaitingEcho = false;

        var bs = Math.Max(FcPolicy.BS, 0);
        _rx.BlockRemaining = bs == 0 ? int.MaxValue : bs;
    }

    private void CompleteReceive(List<IsoTpAction> actions)
    {
        if (_rx.Owner is null)
        {
            AbortReceiveCore();
            return;
        }

        var owner = _rx.Owner;

        _rx.Owner = null;
        _rx.State = RxState.Idle;
        _rx.ExpectedLength = 0;
        _rx.ReceivedBytes = 0;
        _rx.NextSn = 0;
        _rx.BlockRemaining = 0;
        _rx.FcPending = false;
        _rx.FcAwaitingEcho = false;

        actions.Add(new IsoTpAction.EmitDatagram(owner));
    }

    private void AbortReceiveCore()
    {
        _rx.Reset();
    }

    private void EmitDatagram(IMemoryOwner<byte> owner)
    {
        var datagram = new IsoTpDatagram(owner, Endpoint);
        var handler = Volatile.Read(ref DatagramReceived);
        if (handler is null)
        {
            datagram.Dispose();
            return;
        }

        handler(this, datagram);
    }

    private TimeSpan ComputeWaitTime(long nowTicks, TimeSpan globalGuard)
    {
        if (_tx.Current is null || _tx.State != TxState.SendingCF || _tx.AwaitingEcho)
            return TimeSpan.MaxValue;

        return ComputeCfWait(nowTicks, globalGuard);
    }

    private TimeSpan ComputeCfWait(long nowTicks, TimeSpan globalGuard)
    {
        var interval = globalGuard > _tx.Stmin ? globalGuard : _tx.Stmin;
        if (interval <= TimeSpan.Zero || _tx.LastCfTicks == 0)
            return TimeSpan.Zero;

        var elapsed = TimeSpan.FromSeconds((nowTicks - _tx.LastCfTicks) / (double)Stopwatch.Frequency);
        return elapsed >= interval ? TimeSpan.Zero : interval - elapsed;
    }

    private static ReadOnlySpan<byte> GetFramePayload(in CanFrame frame, IsoTpEndpoint endpoint, Pci pci)
    {
        var start = ComputePayloadStart(frame, endpoint, pci);
        if (start >= frame.Data.Length)
            return ReadOnlySpan<byte>.Empty;
        return frame.Data.Span.Slice(start);
    }

    private static int ComputePayloadStart(in CanFrame frame, IsoTpEndpoint endpoint, Pci pci)
    {
        var baseOffset = endpoint.AddrUsePayload ? 1 : 0;
        var dataLen = frame.Data.Length;

        if (baseOffset >= dataLen)
            return dataLen;

        return pci.Type switch
        {
            PciType.SF => baseOffset + (NeedsExtendedSfHeader(frame, baseOffset) ? 2 : 1),
            PciType.FF => baseOffset + (NeedsExtendedFfHeader(frame, baseOffset) ? 6 : 2),
            PciType.CF => baseOffset + 1,
            _ => baseOffset
        };
    }

    private static bool NeedsExtendedSfHeader(in CanFrame frame, int baseOffset)
    {
        var data = frame.Data.Span;
        return frame.FrameKind == CanFrameType.CanFd &&
               baseOffset < data.Length &&
               (data[baseOffset] & 0xF) == 0;
    }

    private static bool NeedsExtendedFfHeader(in CanFrame frame, int baseOffset)
    {
        var data = frame.Data.Span;
        return frame.FrameKind == CanFrameType.CanFd &&
               baseOffset + 1 < data.Length &&
               (data[baseOffset] & 0xF) == 0 &&
               data[baseOffset + 1] == 0;
    }

    private static int CalcSfPayloadMax(bool canFd, bool addrInPayload)
    {
        var baseOffset = addrInPayload ? 1 : 0;

        if (!canFd)
            return 8 - baseOffset - 1;

        // CAN FD 单帧建议统一按扩展 SF header 算，
        // 这样 payload 上限就是 64 - addr - 2
        return 64 - baseOffset - 2;
    }

    private static int CalcFfPayloadMax(bool canFd, bool addrInPayload, int totalPayloadLength)
    {
        var baseOffset = addrInPayload ? 1 : 0;

        if (!canFd)
            return 8 - baseOffset - 2;

        // 当总长度 > 0xFFF 时，FF 要走扩展长度格式，header 为 6 字节
        var useExtendedFfHeader = totalPayloadLength > 0xFFF;
        return 64 - baseOffset - (useExtendedFfHeader ? 6 : 2);
    }

    private static int CalcCfPayloadMax(bool canFd, bool addrInPayload)
    {
        var baseOffset = addrInPayload ? 1 : 0;
        return (canFd ? 64 : 8) - baseOffset - 1;
    }

    #endregion

    public void Dispose()
    {
        lock (_gate)
        {
            while (_readyControlFrames.Count > 0)
            {
                var item = _readyControlFrames.Dequeue();
                try { item.Frame.Dispose(); } catch { /* ignore */ }
            }

            while (_readyDataFrames.Count > 0)
            {
                var item = _readyDataFrames.Dequeue();
                try { item.Frame.Dispose(); } catch { /* ignore */ }
            }

            while (_txPending.Count > 0)
            {
                var op = _txPending.Dequeue();
                try { op.Dispose(); } catch { /* ignore */ }
            }

            if (_tx.Current is not null)
            {
                try { _tx.Current.Dispose(); } catch { /* ignore */ }
            }

            _tx.Reset();
            AbortReceiveCore();
        }
    }
}
