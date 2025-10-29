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
internal enum RxState { Idle, RecvCf, Failed }

internal sealed class IsoTpChannelCore : IDisposable
{
    public IsoTpEndpoint Endpoint { get; }
    public RxFcPolicy FcPolicy { get; }

    private TxState _tx = TxState.Idle;
    private int _txTotalLen, _txSent;
    private byte _sn;
    private int _bsCur, _bsCnt;
    private TimeSpan _stmin = TimeSpan.Zero;
    private long _lastCfTxTicks;

    private readonly Deadline _nAs = new();
    private readonly Deadline _nBs = new();
    private readonly Deadline _nCs = new();

    private RxState _rx = RxState.Idle;
    private int _rxWritten;
    private byte _expectSn = 1;
    private readonly Deadline _nCr = new();

    private readonly ConcurrentQueue<ICanFrame> _pendingFc = new();
    private readonly ConcurrentQueue<TxOperation> _pendingMsg = new();

    private IBufferAllocator _allocator;

    private sealed class TxOperation : IDisposable
    {
        public TxOperation(CancellationTokenRegistration ctr)
        {
            Ctr = ctr;
        }
        public void Dispose()
        {
            while (PendingData.Count != 0)
            {
                PendingData.Dequeue().Dispose();
            }
            Ctr.Dispose();
        }

        public TaskCompletionSource<bool> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Ctr { get; }

        public Queue<ICanFrame> PendingData { get; } = new();

        private volatile bool _cancelRequested;
    }


    public IsoTpChannelCore(IsoTpEndpoint ep, RxFcPolicy policy, IBufferAllocator allocator)
    {
        Endpoint = ep;
        FcPolicy = policy;
        _allocator = allocator;
    }

    public bool Match(in CanReceiveData rx)
    {
        return rx.CanFrame.IsExtendedFrame == Endpoint.IsExtendedId &&
               rx.CanFrame.ID == Endpoint.RxId &&
               (!Endpoint.IsExtendedAddress || (Endpoint.SourceAddress == rx.CanFrame.Data.Span[0]));
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

    private void OnRxSF(in CanReceiveData rx, Pci pci)
    {
        // TODO: 直接上交到 IsoTpChannel（完整PDU）
    }

    private void OnRxFF(in CanReceiveData rx, Pci pci)
    {
        // TODO: 分配缓冲，拷贝首段；根据 FcPolicy 立即入队 FC(CTS)
        _rx = RxState.RecvCf; _expectSn = 1; _nCr.Arm(/*N_Cr*/ TimeSpan.FromMilliseconds(1000));
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
                _bsCur = pci.BS; _bsCnt = 0; _stmin = pci.STmin; _tx = TxState.SendCf; break;
            case FlowStatus.WT:
                _nBs.Arm(/*N_Bs*/ TimeSpan.FromMilliseconds(1000)); break;
            case FlowStatus.OVFLW:
                _tx = TxState.Failed; /* TODO: 报错 */ break;
        }
    }

    public bool IsReadyToSendData(long nowTicks, TimeSpan? globalGuard)
    {
        // 满足：非 WAIT_FC；上次CF距今 ≥ max(STmin, globalGuard)
        if (_tx != TxState.SendCf && _tx != TxState.Idle) return false;
        // TODO: 判断 STmin/GlobalBusGuard
        return !_pendingMsg.IsEmpty;
    }

    public ICanFrame? DequeueFC()
    {
        if (_pendingFc.TryDequeue(out var poolFrame))
        {
            return poolFrame;
        }
        return null;
    }

    public bool TryPeekData(out ICanFrame frame)
    {
        //var re = _pendingData.TryPeek(out var pf);
        //frame = pf;
        //return re;
        frame = null;
        return false;
    }

    public ICanFrame? DequeueData()
    {
        /*
        if (_pendingData.TryDequeue(out var poolFrame))
        {
            return poolFrame;
        }
        */
        return null;
    }

    public void BeginSend(ReadOnlySpan<byte> pdu, bool padding, bool canfd, TimeSpan nBs)
    {
        if (_tx != TxState.Idle) throw new IsoTpException(IsoTpErrorCode.Busy, "Channel busy", Endpoint);
        if (pdu.Length <= CalcSfMax(canfd, Endpoint.Addressing))
        {
            //_pendingMsg.Enqueue(new TxOperation());
            //_pendingData.Enqueue(FrameCodec.BuildSF(Endpoint, _allocator, pdu, padding, canfd));
            return;
        }
        //_pendingData.Enqueue(FrameCodec.BuildFF(Endpoint, _allocator, pdu.Length, pdu, padding));
        _tx = TxState.WaitFc;
        _nBs.Arm(nBs);
        _txTotalLen = pdu.Length;
        _sn = 1;
        // TODO:分包

        int CalcSfMax(bool canfd, Addressing addr) => canfd ? 62 : (addr == Addressing.Extended ? 6 : 7);
    }


    private void EnqueueFC(FlowStatus fs)
    {
        var bs = (byte)FcPolicy.BS;
        var st = FrameCodec.EncodeStmin(FcPolicy.STmin);
        _pendingFc.Enqueue(FrameCodec.BuildFC(Endpoint, _allocator, fs, bs, st, /*padding*/true, /*canfd*/false));
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

    public void Dispose()
    {
        while (_pendingFc.TryDequeue(out var result))
        {
            try { result.Dispose(); } catch { /*Ignored*/ }
        }
    }
}
