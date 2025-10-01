using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils
{
    /// <summary>
    /// software periodic transmit (cross-platform).
    /// 绝对时间+混合等待的软件定时（跨平台）。
    /// </summary>
    public sealed class SoftwarePeriodicTx : IPeriodicTx, IDisposable
    {
        // —— 字段 ——
        private readonly ICanBus _bus;
        private readonly CancellationTokenSource _cts = new();
        private readonly bool _fireImmediately;
        private readonly object _gate = new();

        private CanTransmitData _frame;

        private long _jitterMin, _jitterMax, _jitterLastNs, _jitterSumNs, _jitterCount;
        private TimeSpan _period;
        private int _remaining; // -1 为无限
        private int _repeat;
        private volatile bool _running;
        private Task? _task;

        private SoftwarePeriodicTx(ICanBus bus, CanTransmitData frame, PeriodicTxOptions options)
        {
            _bus = bus;
            _frame = frame;
            _period = options.Period <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : options.Period;
            _remaining = options.Repeat;
            _repeat = options.Repeat;
            _fireImmediately = options.FireImmediately;

            _jitterMin = long.MaxValue;
            _jitterMax = long.MinValue;

        }

        public bool IsRunning => _running;
        public TimeSpan Period => _period;
        public int RepeatCount => _repeat;
        public int RemainingCount => _remaining;

        public event EventHandler? Completed;

        public void Stop()
        {
            try
            {
                _cts.Cancel();
                _task?.Wait(200);
            }
            catch { }
            finally
            {
                _running = false;
            }
        }

        public void Update(CanTransmitData? frame = null, TimeSpan? period = null, int? repeatCount = null)
        {
            lock (_gate)
            {
                if (frame is not null) _frame = frame.Value;
                if (period.HasValue && period.Value > TimeSpan.Zero) _period = period.Value;
                if (repeatCount.HasValue)
                {
                    _remaining = repeatCount.Value;
                    _repeat = repeatCount.Value;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _cts.Dispose();
        }

        public static IPeriodicTx Start(ICanBus bus, CanTransmitData frame, PeriodicTxOptions options)
        {
            var h = new SoftwarePeriodicTx(bus, frame, options);
            h.Start();
            return h;
        }

        public void Start()
        {
            if (_task != null) return;
            _running = true;
            _task = Task.Factory.StartNew(Loop, _cts.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void Loop()
        {
            TryRaiseThreadPriority();

            var token = _cts.Token;
            var sw = Stopwatch.StartNew();
            var period = _period;

            // 目标时刻（绝对对齐）
            var next = sw.Elapsed;

            if (_fireImmediately)
            {
                TrySendOnce();
                if (DecreaseAndMaybeFinish()) { _running = false; return; }
                next = sw.Elapsed; // 立即发了一次，下一拍从此刻对齐
            }

            // 为避免“漏拍累计”，记录起点
            var t0 = sw.Elapsed;
            long n = 1; // 已经排程到的拍数（下一个目标= t0 + n*period）

            while (!token.IsCancellationRequested)
            {
                // 读取最新 period（支持动态 Update）
                lock (_gate) period = _period;
                var target = t0 + TimeSpan.FromTicks(period.Ticks * n);

                // 1) 混合等待：粗睡眠 -> 细让出 -> 自旋微调
                BusyWaitUntil(sw, target, token);

                // 2) 真正发送
                var sendStart = sw.Elapsed;
                TrySendOnce();
                var sendEnd = sw.Elapsed;

                // 3) 抖动记录（理想边沿 vs 实际发送时刻）
                RecordJitter(sendStart - target);

                // 4) 次数控制
                if (DecreaseAndMaybeFinish()) break;

                // 5) 下一拍（绝对对齐；若当前已晚于多拍，跳过到最近一拍）
                n = Math.Max(n + 1, (long)Math.Floor((sw.Elapsed - t0).Ticks / (double)period.Ticks) + 1);
            }

            _running = false;
        }

        // 混合等待策略：>1ms 用 Sleep(x)；1ms~100µs 用 Sleep(0)；<100µs 自旋
        private static void BusyWaitUntil(Stopwatch sw, TimeSpan target, CancellationToken token)
        {
            const int CoarseMs = 1;           // 粗睡眠阈值（跨平台稳妥）
            const int FineYieldUs = 1000;     // 细让出阈值（~1ms）
            const int SpinUs = 100;           // 自旋阈值（~100µs）

            while (!token.IsCancellationRequested)
            {
                var remain = target - sw.Elapsed;
                if (remain <= TimeSpan.Zero) break;

                if (remain.TotalMilliseconds > CoarseMs)
                {
                    Thread.Sleep(1);          // 粗等，避免长时间自旋
                }
                else if (remain.TotalMilliseconds > 0.001) // >1µs
                {
                    // 细让出：Sleep(0) 让出时间片但尽快回来
                    Thread.Sleep(0);
                }
                else
                {
                    // 最后几十微秒以内自旋
                    Thread.SpinWait(64);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrySendOnce()
        {
            try
            {
                CanTransmitData frame;
                lock (_gate) { frame = _frame; }
                // 假定 _bus.Transmit 是非阻塞/快速路径；如有队列返回值可检查
                _ = _bus.Transmit(new[] { frame });
            }
            catch { /* 避免后台异常炸掉周期 */ }
        }

        private bool DecreaseAndMaybeFinish()
        {
            lock (_gate)
            {
                if (_remaining == 0)
                    return false; // 已经结束态（有限次用完）
                if (_remaining > 0)
                {
                    _remaining--;
                    if (_remaining == 0)
                    {
                        Completed?.Invoke(this, EventArgs.Empty);
                        Stop();
                        return true;
                    }
                }
            }
            return false;
        }

        // —— 抖动统计（可选） ——
        private void RecordJitter(TimeSpan delta)
        {
            // 以纳秒为单位统计：负值=提前，正值=滞后
            long ns = (long)(delta.TotalMilliseconds * 1_000_000.0);
            Interlocked.Exchange(ref _jitterLastNs, ns);
            InterlockedExtensions.Min(ref _jitterMin, ns);
            InterlockedExtensions.Max(ref _jitterMax, ns);
            Interlocked.Add(ref _jitterCount, 1);
            Interlocked.Add(ref _jitterSumNs, ns);
        }

        public (long lastNs, long minNs, long maxNs, long count, long avgNs) GetJitterStats()
        {
            var c = Interlocked.Read(ref _jitterCount);
            var sum = Interlocked.Read(ref _jitterSumNs);
            var avg = c > 0 ? sum / c : 0;
            return (_jitterLastNs, _jitterMin == long.MaxValue ? 0 : _jitterMin,
                    _jitterMax == long.MinValue ? 0 : _jitterMax, c, avg);
        }

        private static void TryRaiseThreadPriority()
        {
            try
            {
                // 跨平台保守做法：High；在容器/低权限环境可能无效
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            }
            catch { }
        }
    }

    internal static class InterlockedExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Min(ref long location, long value)
        {
            long initial, computed;
            do
            {
                initial = Volatile.Read(ref location);
                computed = Math.Min(initial, value);
                if (computed == initial) return;
            } while (Interlocked.CompareExchange(ref location, computed, initial) != initial);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Max(ref long location, long value)
        {
            long initial, computed;
            do
            {
                initial = Volatile.Read(ref location);
                computed = Math.Max(initial, value);
                if (computed == initial) return;
            } while (Interlocked.CompareExchange(ref location, computed, initial) != initial);
        }
    }

}
