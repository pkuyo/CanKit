using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Diagnostics;
using CanKit.Protocol.IsoTp.Utils;

namespace CanKit.Protocol.IsoTp.Core;

internal enum TxState { Idle, WaitFc, SendCf, WaitFcAfterBlock, Failed }
internal enum RxState { Idle, RecvCf, Failed }

public sealed class IsoTpChannelCore
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
    private IMemoryOwner<byte>? _rxBufOwner;
    private Memory<byte> _rxBuf;
    private int _rxWritten;
    private byte _expectSn = 1;
    private readonly Deadline _nCr = new();

    private readonly ConcurrentQueue<ICanFrame> _pendingFc = new();
    private readonly ConcurrentQueue<PoolFrame> _pendingData = new();

    public IsoTpChannelCore(IsoTpEndpoint ep, RxFcPolicy policy)
    {
        Endpoint = ep; FcPolicy = policy;
    }

    public bool Match(in CanReceiveData rx)
    {
        // TODO: 严格匹配 {CAN_ID==Endpoint.RxId, IDE==Endpoint.IsExtendedId, N_AI?}
        return false;
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
        return _pendingData.IsEmpty;
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

    public void BeginSend(ReadOnlySpan<byte> pdu, bool padding, bool canfd, TimeSpan nBs)
    {
        if (_tx != TxState.Idle) throw new IsoTpException(IsoTpErrorCode.Busy, "Channel busy", Endpoint);
        if (pdu.Length <= CalcSfMax(canfd, Endpoint.Addressing))
        {
            _pendingData.Enqueue(FrameCodec.BuildSF(Endpoint, pdu, padding, canfd));
            return;
        }
        // FF + 后续 CF 分段入队（这里只入队FF；CF按 FC 后再分段入队亦可）
        _pendingData.Enqueue(FrameCodec.BuildFF(Endpoint, pdu.Length, pdu, padding));
        _tx = TxState.WaitFc;
        _nBs.Arm(nBs);
        _txTotalLen = pdu.Length;
        //_txSent = first.Length;
        _sn = 1;
        // 后续 CF 分段可延迟生成（依据 STmin/BS），或一次性预拆（简单但占内存）

        int CalcSfMax(bool canfd, Addressing addr) => canfd ? 62 : (addr == Addressing.Extended ? 6 : 7);
    }
}
