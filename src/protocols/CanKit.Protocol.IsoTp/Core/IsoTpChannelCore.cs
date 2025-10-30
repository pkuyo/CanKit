using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Diagnostics;
using CanKit.Protocol.IsoTp.Utils;

namespace CanKit.Protocol.IsoTp.Core;

internal enum TxState { Idle, WaitFc, SendCf, WaitFcAfterBlock, Failed }
internal enum RxState { Idle, RecvCf }

internal sealed class IsoTpChannelCore : IDisposable
{
    internal sealed class TxOperation : IDisposable
    {

        private volatile bool _cancelRequested;

        private readonly Queue<ICanFrame> _pendingData = new();

        public CancellationTokenRegistration Ctr { get; set; }

        public bool Canceled => _cancelRequested;

        public bool Empty => _pendingData.Count == 0;

        public int TxAddress { get; }
        public int? ExtendAddress { get; }

        public TaskCompletionSource<bool> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTime StartedAt { get; } = DateTime.Now;

        public TxOperation(int txAddress, int? extendAddress)
        {
            TxAddress = txAddress;
            ExtendAddress = extendAddress;
        }

        public void Enqueue(ICanFrame canFrame)
        {
            _pendingData.Enqueue(canFrame);
        }

        public ICanFrame Dequeue()
        {
            return _pendingData.Dequeue();
        }

        public bool TryPeek(out ICanFrame? canFrame)
        {
#if NET5_0_OR_GREATER
            return _pendingData.TryPeek(out canFrame);
#else
            if (_pendingData.Count == 0)
            {
                canFrame = _pendingData.Peek();
                return true;
            }

            canFrame = null;
            return false;
#endif
        }

        public void Dispose()
        {
            _cancelRequested = true;
            while (_pendingData.Count != 0)
            {
                _pendingData.Dequeue().Dispose();
            }
            Ctr.Dispose();
        }

    }

    private readonly struct FcStat
    {

    }

    private TxState _tx = TxState.Idle;
    private RxState _rx = RxState.Idle;

    private readonly Deadline _nAs = new();
    private readonly Deadline _nBs = new();
    private readonly Deadline _nCs = new();
    private readonly Deadline _nCr = new();


    private readonly ConcurrentQueue<TxOperation> _pendingFc = new();
    private readonly ConcurrentQueue<TxOperation> _pendingOperations = new();

    private readonly IBufferAllocator _allocator;

    public IsoTpEndpoint Endpoint { get; }
    public IsoTpOptions Options { get; }
    public RxFcPolicy FcPolicy { get; }
    public IsoTpChannelCore(IsoTpEndpoint ep, IsoTpOptions options, RxFcPolicy policy, IBufferAllocator allocator)
    {
        Endpoint = ep;
        Options = options;
        FcPolicy = policy;
        _allocator = allocator;
    }

    public bool Match(in CanReceiveData rx)
    {
        return rx.CanFrame.IsExtendedFrame == Endpoint.IsExtendedId &&
               rx.CanFrame.ID == Endpoint.RxId &&
               (!Endpoint.IsExtendedAddress || (Endpoint.RxAddress == rx.CanFrame.Data.Span[0]));
    }

    public bool Match(in TxOperation tx)
    {
        return tx.TxAddress ==  Endpoint.TxId &&
               tx.ExtendAddress == Endpoint.TxAddress;
    }

    public void OnRx(in CanReceiveData rx)
    {
        if (!FrameCodec.TryParsePci(rx, Endpoint, out var pci)) return;

        switch (pci.Type)
        {
            case PciType.SF: OnRxSF(rx, pci); break;
            case PciType.FF: OnRxFF(rx, pci); break;
            case PciType.CF: OnRxCF(rx, pci); break;
            case PciType.FC: OnRxFC(pci); break;
        }
    }

    public void OnTx(TxOperation operation)
    {
        operation.Tcs.SetResult(true);
        operation.Dispose();
        if (TryPeekOperation(out var opera) && opera == operation)
        {
            _pendingOperations.TryDequeue(out _);
        }
    }

    public void OnTxFailed(TxOperation operation, Exception exception)
    {
        operation.Tcs.SetException(exception);
        operation.Dispose();
        if (TryPeekOperation(out var opera) && opera == operation)
        {
            _pendingOperations.TryDequeue(out _);
        }
    }

    private void OnRxSF(in CanReceiveData rx, Pci pci)
    {
        // TODO: 直接上交到 IsoTpChannel（完整PDU）
    }

    private void OnRxFF(in CanReceiveData rx, Pci pci)
    {
        // TODO: 分配缓冲，拷贝首段；根据 FcPolicy 立即入队 FC(CTS)
        //_rx = RxState.RecvCf; _expectSn = 1;
        _nCr.Arm(/*N_Cr*/ TimeSpan.FromMilliseconds(1000));
    }

    private void OnRxCF(in CanReceiveData rx, Pci pci)
    {
        if (_rx != RxState.RecvCf) return;
        // TODO: 校验 SN，拷贝数据，_nCr.Arm(N_Cr)；块满时 Enqueue FC(CTS/WT/OVFLW)
        // 完成时上交完整PDU并回 Idle
    }

    private void OnRxFC(Pci pci)
    {
        if (_tx is not (TxState.WaitFc or TxState.WaitFcAfterBlock)) return;
        switch (pci.FS)
        {
            case FlowStatus.CTS:
                //_bsCur = pci.BS; _bsCnt = 0; _stmin = pci.STmin; _tx = TxState.SendCf;
                break;
            case FlowStatus.WT:
                _nBs.Arm(/*N_Bs*/ TimeSpan.FromMilliseconds(1000));
                break;
            case FlowStatus.OVFLW:
                _tx = TxState.Failed; /* TODO: 报错 */ break;
        }
    }



    public bool IsReadyToSendData(long nowTicks, TimeSpan? globalGuard)
    {
        // 满足：非 WAIT_FC；上次CF距今 ≥ max(STmin, globalGuard)
        if (_tx != TxState.SendCf && _tx != TxState.Idle) return false;
        // TODO: 判断 STmin/GlobalBusGuard
        return !_pendingOperations.IsEmpty;
    }

    public Task<bool> SendAsync(ReadOnlySpan<byte> data, bool padding, bool canFd, CancellationToken ct)
    {
        if (_tx != TxState.Idle)
        {
            return Task.FromException<bool>(new IsoTpException(IsoTpErrorCode.Busy, "Channel busy", Endpoint));
        }

        var operation = new TxOperation(Endpoint.TxId, Endpoint.TxAddress);
        operation.Ctr = ct.Register(() => OnTxFailed(operation, new OperationCanceledException("User canceled send")));
        _pendingOperations.Enqueue(operation);

        var sfMax = CalcSfMax(canFd, Endpoint.IsExtendedAddress);
        if (data.Length <= sfMax)
        {
            operation.Enqueue(FrameCodec.BuildSF(Endpoint, _allocator, data, padding, canFd));
        }
        else
        {
            operation.Enqueue(FrameCodec.BuildFF(Endpoint, _allocator, data.Length, data, canFd));
            _tx = TxState.WaitFc;
            int index = sfMax;
            int cfLen = CalcCfMax(canFd, Endpoint.IsExtendedAddress);
            byte sn = 0;
            while (index < data.Length)
            {
                operation.Enqueue(FrameCodec.BuildCF(Endpoint, _allocator, sn, data.Slice(index), padding, canFd));
                sn = (byte)((sn + 1) % 16);
                index += cfLen;
            }
        }

        return WaitSendAsync(operation.Tcs);

        async Task<bool> WaitSendAsync(TaskCompletionSource<bool> tcs)
            => await tcs.Task;

        int CalcSfMax(bool fd, bool extendAddress)
            => fd ? 62 : (extendAddress ? 6 : 7);

        int CalcCfMax(bool fd, bool extendAddress)
            => (fd ? 63 : 7) - (extendAddress ? 1 : 0);
    }


    private void EnqueueFC(FlowStatus fs)
    {
        var bs = (byte)FcPolicy.BS;
        var st = FrameCodec.EncodeStmin(FcPolicy.STmin);
        var txOperation = new TxOperation(Endpoint.TxId, Endpoint.TxAddress);
        txOperation.Enqueue(
            FrameCodec.BuildFC(Endpoint, _allocator, fs, bs, st, /*padding*/true,
                /*canfd*/false));
        _pendingFc.Enqueue(txOperation);
    }
    public void ProcessTimers()
    {
        if (_tx == TxState.WaitFc && _nBs.Expired())
            throw new IsoTpException(IsoTpErrorCode.Timeout_N_Bs, "Wait FC timeout", Endpoint);
        if (_tx == TxState.WaitFcAfterBlock && _nCs.Expired())
            throw new IsoTpException(IsoTpErrorCode.Timeout_N_Cs, "Wait next FC timeout", Endpoint);
        if (_rx == RxState.RecvCf && _nCr.Expired())
            throw new IsoTpException(IsoTpErrorCode.Timeout_N_Cr, "Wait CF timeout", Endpoint);
    }

    public TxOperation? TryDequeueFC()
    {
        if (_pendingFc.TryDequeue(out var poolFrame))
        {
            return poolFrame;
        }
        return null;
    }

    public bool TryPeekOperation(out TxOperation operation)
    {
        return _pendingOperations.TryPeek(out operation);
    }
    public bool TryPeekData(out ICanFrame? frame)
    {
        frame = null;
        if (TryPeekOperation(out var txOperation))
        {
            return txOperation.TryPeek(out frame);
        }

        return false;
    }

    public void Dispose()
    {
        while (_pendingFc.TryDequeue(out var result))
        {
            try { result.Dispose(); } catch { /*Ignored*/ }
        }
    }
}
