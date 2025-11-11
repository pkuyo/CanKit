using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport.Definitions;
using CanKit.Abstractions.API.Transport.Excpetions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Utils;

namespace CanKit.Transport.IsoTp.Core;

internal enum TxState { Idle, WaitFc, SendCf, WaitFcAfterBlock, Failed }
internal enum RxState { Idle, RecvCf }

internal sealed class IsoTpChannelCore : IDisposable
{
    internal event EventHandler<IsoTpDatagram>? DatagramReceived;

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

    private long _lastCfTicks;
    private TimeSpan _currentStmin;

    private IMemoryOwner<byte>? _rxOwner;
    private int _rxExpectedLength;
    private int _rxReceivedBytes;
    private byte _rxNextSn;
    private int _rxBlockRemaining;

    public IsoTpEndpoint Endpoint { get; }
    public IsoTpOptions Options { get; }
    public RxFcPolicy FcPolicy { get; }
    public IsoTpChannelCore(IsoTpEndpoint ep, IsoTpOptions options, RxFcPolicy policy, IBufferAllocator allocator)
    {
        Endpoint = ep;
        Options = options;
        FcPolicy = policy;
        _allocator = allocator;

        _nAs = new QueuedDeadline(Options.N_As);
        _nAr = new QueuedDeadline(Options.N_Ar);

        _nBs = new Deadline(Options.N_Bs);
        _nBr = new Deadline(Options.N_Br);

        _nCs = new Deadline(Options.N_Cs);
        _nCr = new Deadline(Options.N_Cr);
        _currentStmin = FcPolicy.STmin;
    }

    public bool Match(in CanReceiveData rx)
    {
        // TODO: add payload-based filtering when extended addressing is enabled.
        return rx.CanFrame.IsExtendedFrame == Endpoint.IsExtendedId &&
               rx.CanFrame.ID == Endpoint.RxId;
    }

    public bool Match(in TxOperation tx)
    {
        return tx.TxAddress == Endpoint.TxId &&
               tx.ExtendAddress == Endpoint.TargetAddress;
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
        if (frame.Type is PciType.CF)
        {
            operation.TxCount++;
            _lastCfTicks = Stopwatch.GetTimestamp();
        }

        UpdateTxState(operation, frame.Frame, frame.Type);

        if (operation.Empty)
        {
            operation.Tcs.SetResult(true);
            operation.Dispose();
            if (frame.Type is not PciType.FC)
            {
                Debug.Assert(TryPeekOperation(out var opera) && opera == operation);
                _pendingOperations.TryDequeue(out _);
                _tx = TxState.Idle;
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
        _tx = TxState.Idle;
    }

    private void OnRxSF(in CanReceiveData rx, Pci pci)
    {
        ResetReception();
        var payload = GetFramePayload(rx.CanFrame, Endpoint, pci);
        var length = Math.Min(payload.Length, pci.Len);
        if (length <= 0) return;

        var owner = _allocator.Rent(length);
        payload.Slice(0, length).CopyTo(owner.Memory.Span);
        EmitDatagram(owner);
    }

    private void OnRxFF(in CanReceiveData rx, Pci pci)
    {
        ResetReception();
        if (pci.Len <= 0) return;

        var payload = GetFramePayload(rx.CanFrame, Endpoint, pci);
        var total = pci.Len;
        var copied = Math.Min(payload.Length, total);

        var owner = _allocator.Rent(total);
        if (copied > 0)
        {
            payload.Slice(0, copied).CopyTo(owner.Memory.Span);
        }

        _rxOwner = owner;
        _rxExpectedLength = total;
        _rxReceivedBytes = copied;
        _rxNextSn = 1;
        _rx = RxState.RecvCf;
        _nCr.Reset();

        if (_rxReceivedBytes >= _rxExpectedLength)
        {
            CompleteReception();
            return;
        }

        QueueFlowControlCts();
    }

    private void OnRxCF(in CanReceiveData rx, Pci pci)
    {
        if (_rx != RxState.RecvCf || _rxOwner is null) return;
        if (_rxExpectedLength <= 0)
        {
            ResetReception();
            return;
        }

        if (pci.SN != _rxNextSn)
        {
            ResetReception();
            return;
        }

        var payload = GetFramePayload(rx.CanFrame, Endpoint, pci);
        var remaining = _rxExpectedLength - _rxReceivedBytes;
        if (remaining <= 0)
        {
            CompleteReception();
            return;
        }

        var take = Math.Min(payload.Length, remaining);
        if (take > 0)
        {
            payload.Slice(0, take).CopyTo(_rxOwner.Memory.Span.Slice(_rxReceivedBytes));
            _rxReceivedBytes += take;
        }

        _rxNextSn = (byte)((pci.SN + 1) % IsoTpConst.SN_Mod);
        _nCr.Reset();

        if (_rxBlockRemaining != int.MaxValue)
        {
            if (_rxBlockRemaining > 0)
            {
                _rxBlockRemaining--;
            }

            if (_rxBlockRemaining <= 0 && _rxReceivedBytes < _rxExpectedLength)
            {
                QueueFlowControlCts();
            }
        }

        if (_rxReceivedBytes >= _rxExpectedLength)
        {
            CompleteReception();
        }
    }

    private void OnRxFC(Pci pci)
    {
        if (_tx is not (TxState.WaitFc or TxState.WaitFcAfterBlock)) return;
        switch (pci.FS)
        {
            case FlowStatus.CTS:
                _nBs.Reset();
                _currentStmin = pci.STmin;
                if (TryPeekOperation(out var operation))
                {
                    operation.BS = pci.BS;
                    operation.TxCount = 0;
                    _tx = TxState.SendCf;
                }
                break;
            case FlowStatus.WT:
                _nBs.Restart();
                break;
            case FlowStatus.OVFLW:
                _tx = TxState.Failed; /* TODO: 报错 */ break;
        }
    }

    private void ResetReception()
    {
        if (_rxOwner is not null)
        {
            _rxOwner.Dispose();
            _rxOwner = null;
        }
        _rxExpectedLength = 0;
        _rxReceivedBytes = 0;
        _rxNextSn = 0;
        _rxBlockRemaining = 0;
        _rx = RxState.Idle;
    }

    private void CompleteReception()
    {
        if (_rxOwner is null) return;
        var owner = _rxOwner;
        _rxOwner = null;
        _rxExpectedLength = 0;
        _rxReceivedBytes = 0;
        _rxNextSn = 0;
        _rxBlockRemaining = 0;
        _rx = RxState.Idle;
        EmitDatagram(owner);
    }
    private void QueueFlowControlCts()
    {
        EnqueueFC(FlowStatus.CTS);
        var blockSize = FcPolicy.BS;
        if (blockSize <= 0)
        {
            _rxBlockRemaining = int.MaxValue;
            return;
        }

        _rxBlockRemaining = blockSize;
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

    private static ReadOnlySpan<byte> GetFramePayload(in CanFrame frame, IsoTpEndpoint endpoint, Pci pci)
    {
        var start = ComputePayloadStart(frame, endpoint, pci);
        if (start >= frame.Data.Length)
        {
            return ReadOnlySpan<byte>.Empty;
        }
        return frame.Data.Span.Slice(start);
    }

    private static int ComputePayloadStart(in CanFrame frame, IsoTpEndpoint endpoint, Pci pci)
    {
        var baseOffset = endpoint.UsePayload ? 1 : 0;
        var dataLen = frame.Data.Length;
        if (baseOffset >= dataLen)
        {
            return dataLen;
        }
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

    public bool IsReadyToSendData(long nowTicks, TimeSpan? globalGuard)
    {
        if (_pendingOperations.IsEmpty) return false;
        if (_tx is TxState.WaitFc or TxState.WaitFcAfterBlock or TxState.Failed) return false;
        if (_tx == TxState.SendCf)
        {
            var guard = globalGuard ?? TimeSpan.Zero;
            var interval = guard > _currentStmin ? guard : _currentStmin;
            if (interval <= TimeSpan.Zero || _lastCfTicks == 0) return true;
            var elapsed = TimeSpan.FromSeconds((nowTicks - _lastCfTicks) / (double)Stopwatch.Frequency);
            return elapsed >= interval;
        }
        return true;
    }

    public Task<bool> SendAsync(ReadOnlySpan<byte> data, bool padding, bool canFd, CancellationToken ct)
    {
        if (_tx != TxState.Idle)
        {
            return Task.FromException<bool>(new IsoTpException(IsoTpErrorCode.Busy, "Channel busy", Endpoint));
        }

        var operation = new TxOperation(Endpoint.TxId, Endpoint.TargetAddress);
        operation.Ctr = ct.Register(() => OnTxFailed(operation, default, new OperationCanceledException("User canceled send")),
            false);
        _pendingOperations.Enqueue(operation);

        var sfMax = CalcSfMax(canFd, Endpoint.UsePayload);
        if (data.Length <= sfMax)
        {
            operation.Enqueue(FrameCodec.BuildSF(Endpoint, _allocator, data, padding, canFd), PciType.SF);
        }
        else
        {
            operation.Enqueue(FrameCodec.BuildFF(Endpoint, _allocator, data.Length, data, canFd), PciType.FF);
            _tx = TxState.WaitFc;
            int index = sfMax;
            int cfLen = CalcCfMax(canFd, Endpoint.UsePayload);
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
        var txOperation = new TxOperation(Endpoint.TxId, Endpoint.TargetAddress);
        txOperation.Enqueue(
            FrameCodec.BuildFC(Endpoint, _allocator, fs, bs, st, /*padding*/true,
                /*canfd*/false),
            PciType.FC);
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
        ResetReception();
    }
}
