using System.Diagnostics;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;
using CanKit.Protocol.IsoTp.Defines;
using CanKit.Protocol.IsoTp.Diagnostics;

namespace CanKit.Protocol.IsoTp.Core;


internal sealed class IsoTpScheduler
{
    private readonly ICanBus _can;
    private readonly bool canTxTimeout;
    private readonly IsoTpOptions _opt;
    private readonly Router _router = new();
    private readonly List<IsoTpChannelCore> _channels = new();
    private readonly List<(double score, IsoTpChannelCore ch)> _candidates = new();
    private long _lastDataTxTicks;

    private Stopwatch N_AsStopWatch = new();

    public IsoTpScheduler(ICanBus can, IsoTpOptions opt)
    {
        _can = can;
        _opt = opt;
        canTxTimeout = _can.Options.Features.HasFlag(CanFeature.TxTimeOut);
    }

    public void Register(IsoTpChannelCore ch) { _channels.Add(ch); _router.Register(ch); }
    public void Unregister(IsoTpChannelCore ch) { _channels.Remove(ch); _router.Unregister(ch); }

    public void TransmitWithAs(PoolFrame poolFrame)
    {
        if (canTxTimeout)
        {
            if (_can.Transmit([poolFrame.CanFrame], _opt.N_As.Milliseconds) == 0)
                throw new IsoTpException(IsoTpErrorCode.Timeout_N_As, "TODO");
        }
        else
        {
            N_AsStopWatch.Restart();
            while (true)
            {
                if (_can.Transmit(poolFrame.CanFrame) == 1)
                    break;
                PreciseDelay.Delay(TimeSpan.FromMilliseconds(1));
                if(N_AsStopWatch.Elapsed > _opt.N_As)
                    throw new IsoTpException(IsoTpErrorCode.Timeout_N_As, "TODO");
            }
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // 订阅底层事件：有帧就路由；后台异常→抛到上层
        _can.FrameReceived += (_, rx) => _router.Route(rx);
        _can.BackgroundExceptionOccurred += (_, ex) => throw new IsoTpException(IsoTpErrorCode.BackgroundException, ex.Message, null, ex);

        while (!ct.IsCancellationRequested)
        {
            // 1) 先发所有 FC（最高优先）
            foreach (var ch in _channels)
            {
                while (true)
                {
                    using var pf = ch.DequeueFC();
                    if (pf is null) break;
                    TransmitWithAs(pf);
                }
            }

            // 2) 定时器巡检
            foreach (var ch in _channels)
            {
                ch.ProcessTimers();
            }

            // 3) 收集候选数据帧
            _candidates.Clear();
            var now = Stopwatch.GetTimestamp();
            foreach (var ch in _channels)
            {
                if (!ch.IsReadyToSendData(now, _opt.GlobalBusGuard)) continue;
                if (!ch.TryPeekData(out var f))
                    continue;
                var score = Score(ch, f, now);
                _candidates.Add((score, ch));
            }

            // 4) 选择一帧（公平 + 防饿），遵守BusGuard（仅对数据帧）
            if (_candidates.Count > 0 && RespectBusGuard(now))
            {
                _candidates.Sort((a, b) => b.score.CompareTo(a.score));
                var (score, ch) = _candidates[0];
                using var dataPf = ch.DequeueData();
                if (dataPf != null)
                {
                    TransmitWithAs(dataPf);
                    _lastDataTxTicks = Stopwatch.GetTimestamp();
                }
            }

            // 5) 稍作让渡
            await Task.Yield();
        }
    }

    private bool RespectBusGuard(long nowTicks)
    {
        if (_opt.GlobalBusGuard is null) return true;
        var elapsed = TimeSpan.FromSeconds((nowTicks - _lastDataTxTicks) / (double)Stopwatch.Frequency);
        return elapsed >= _opt.GlobalBusGuard.Value;
    }

    private static double Score(IsoTpChannelCore ch, ICanFrame f, long nowTicks)
    {
        // 简化的有效分：可按优先级/aging/截止时间扩展
        return nowTicks * 1e-12;
    }
}
