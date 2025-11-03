using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.API.Transport.Excpetions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Utils;
using CanKit.Transport.IsoTp.Options;

namespace CanKit.Protocol.IsoTp.Core;

internal enum TxState { Idle, WaitFc, SendCf, WaitFcAfterBlock, Failed }
internal enum RxState { Idle, RecvCf }

internal sealed class IsoTpChannelCore : IDisposable
{

    internal sealed class TxOperation : IDisposable
    {

        private readonly Queue<TxFrame> _pendingFrames = new();

        public CancellationTokenRegistration Ctr { get; set; }

        public bool Empty => _pendingFrames.Count == 0;

        public int TxAddress { get; }
        public int? ExtendAddress { get; }

        public TaskCompletionSource<bool> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTime StartedAt { get; } = DateTime.Now;

        public int BS { get; set; } = 0;

        public int TxCount { get; set; }

        public TxOperation(int txAddress, int? extendAddress)
        {
            TxAddress = txAddress;
            ExtendAddress = extendAddress;
        }

        public void Enqueue(CanFrame canFrame, PciType type)
            => _pendingFrames.Enqueue(new TxFrame(canFrame, type));

        public TxFrame Dequeue() => _pendingFrames.Dequeue();

        public bool TryPeek(out TxFrame canFrame)
        {
#if NET5_0_OR_GREATER
            return _pendingFrames.TryPeek(out canFrame);
#else
            if (_pendingFrames.Count == 0)
            {
                canFrame = _pendingFrames.Peek();
                return true;
            }

            canFrame = default;
            return false;
#endif
        }

        public void Dispose()
        {
            while (_pendingFrames.Count != 0)
            {
                _pendingFrames.Dequeue().Frame.Dispose();
            }
            Ctr.Dispose();
        }

    }

    internal readonly record struct TxFrame(CanFrame Frame, PciType Type);

    private TxState _tx = TxState.Idle;
    private RxState _rx = RxState.Idle;

    private readonly QueuedDeadline? _nAs;
    private readonly Deadline _nBs;
    private readonly Deadline _nCs;

    private readonly QueuedDeadline? _nAr;
    private readonly Deadline _nBr;
    private readonly Deadline _nCr;


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

        if (options.N_AxCheck)
        {
            _nAs = new QueuedDeadline(Options.N_As);
            _nAr = new QueuedDeadline(Options.N_Ar);
        }
        _nBs = new Deadline(Options.N_Bs);
        _nBr = new Deadline(Options.N_Br);

        _nCs = new Deadline(Options.N_Cs);
        _nCr = new Deadline(Options.N_Cr);
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

    private void UpdateTxDeadline(TxOperation operation, in CanFrame frame, PciType type)
    {
        switch (type)
        {
            case PciType.FF:
                _nAs?.Enqueue(frame);
                _nBs.Restart();
                break;
            case PciType.CF:
                _nAs?.Enqueue(frame);
                _nCs.Restart();
                break;
            case PciType.FC:
                _nAr?.Enqueue(frame);
                break;
        }
    }

    private void UpdateTxState(TxOperation operation, in CanFrame frame, PciType type)
    {
        switch (type)
        {
            case PciType.FF:
                _tx = TxState.WaitFc;
                break;
            case PciType.CF when (operation.TxCount == operation.BS && operation.BS != 0):
                _tx = TxState.WaitFcAfterBlock;
                break;
            case PciType.FC:
                _rx = RxState.RecvCf;
                break;
        }
    }

    private void UpdateRxDeadline(in CanFrame frame, Pci pci)
    {
        switch (pci.Type)
        {
            case PciType.FF:
                _nBr.Restart();
                break;
            case PciType.FC when pci.FS == FlowStatus.CTS:
                _nCs.Restart();
                break;
        }
    }

    public void OnRx(in CanReceiveData rx)
    {
        if (rx.IsEcho)
        {
            if (!Options.N_AxCheck)
                return;
            _nAs?.Dequeue(rx.CanFrame);
            _nAr?.Dequeue(rx.CanFrame);
        }
        else
        {
            if (!FrameCodec.TryParsePci(rx, Endpoint, out var pci)) return;

            UpdateRxDeadline(rx.CanFrame, pci);

            switch (pci.Type)
            {
                case PciType.SF: OnRxSF(rx, pci); break;
                case PciType.FF: OnRxFF(rx, pci); break;
                case PciType.CF: OnRxCF(rx, pci); break;
                case PciType.FC: OnRxFC(pci); break;
            }
        }

    }

    public void OnTx(TxOperation operation, in TxFrame frame)
    {
        if (operation.Empty)
        {
            operation.Tcs.SetResult(true);
            operation.Dispose();
            if (frame.Type is not PciType.FC)
            {
                Debug.Assert(TryPeekOperation(out var opera) && opera == operation);
                _pendingOperations.TryDequeue(out _);
            }
        }

        UpdateTxDeadline(operation, frame.Frame, frame.Type);
    }

    public void OnTxFailed(TxOperation operation, in TxFrame frame, Exception exception)
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
        _nCr.Reset();
    }

    private void OnRxCF(in CanReceiveData rx, Pci pci)
    {
        if (_rx != RxState.RecvCf) return;
    }

    private void OnRxFC(Pci pci)
    {
        if (_tx is not (TxState.WaitFc or TxState.WaitFcAfterBlock)) return;
        switch (pci.FS)
        {
            case FlowStatus.CTS:
                //_bsCur = pci.BS; _bsCnt = 0; _stmin = pci.STmin; _tx = TxState.SendCf;
                _nBs.Reset();
                break;
            case FlowStatus.WT:
                _nBs.Restart();
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
        operation.Ctr = ct.Register(() => OnTxFailed(operation, default, new OperationCanceledException("User canceled send")),
            false);
        _pendingOperations.Enqueue(operation);

        var sfMax = CalcSfMax(canFd, Endpoint.IsExtendedAddress);
        if (data.Length <= sfMax)
        {
            operation.Enqueue(FrameCodec.BuildSF(Endpoint, _allocator, data, padding, canFd), PciType.SF);
        }
        else
        {
            operation.Enqueue(FrameCodec.BuildFF(Endpoint, _allocator, data.Length, data, canFd), PciType.FF);
            _tx = TxState.WaitFc;
            int index = sfMax;
            int cfLen = CalcCfMax(canFd, Endpoint.IsExtendedAddress);
            byte sn = 0;
            while (index < data.Length)
            {
                operation.Enqueue(FrameCodec.BuildCF(Endpoint, _allocator, sn, data.Slice(index), padding, canFd),
                    PciType.CF);
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
                /*canfd*/false), PciType.FC);
        _pendingFc.Enqueue(txOperation);
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
        return _pendingOperations.TryPeek(out operation!);
    }
    public bool TryPeekData(out CanFrame frame)
    {
        frame = default;
        var re = false;
        if (TryPeekOperation(out var txOperation))
        {
            re = txOperation.TryPeek(out var txF);
            frame = txF.Frame;
        }
        return re;
    }

    public void Dispose()
    {
        while (_pendingFc.TryDequeue(out var result))
        {
            try { result.Dispose(); } catch { /*Ignored*/ }
        }
    }
}
