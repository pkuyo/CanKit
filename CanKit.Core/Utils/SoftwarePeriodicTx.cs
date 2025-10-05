using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils
{
    //最小限度2ms，小于2ms可靠性无法保证
    public sealed class SoftwarePeriodicTx : IPeriodicTx, IDisposable
    {
        private readonly ICanBus _bus;
        private readonly CancellationTokenSource _cts = new();
        private readonly bool _fireImmediately;
        private readonly object _gate = new();


        private CanTransmitData _frame;
        private readonly CanTransmitData[] _txBuf = new CanTransmitData[1];

        private long _jitterMin, _jitterMax, _jitterLastNs, _jitterSumNs, _jitterCount;
        private TimeSpan _period;
        private int _remaining; // -1 = 无限
        private int _repeat;
        private volatile bool _running;
        private Task? _task;

        private PlatformContext _ctx;

        private SoftwarePeriodicTx(ICanBus bus, CanTransmitData frame, PeriodicTxOptions options)
        {
            if (options.Period < TimeSpan.FromMilliseconds(2))
            {
                //TODO:输出警告信息，太短的间隔时间不稳定
            }
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

        public void Stop()
        {
            try
            {
                _cts.Cancel();
                if (_task != null && Task.CurrentId != _task.Id)
                    _task.Wait(200);
            }
            catch { /*ignore*/ }
            finally
            {
                _running = false;
                _sDispose(ref _ctx);
            }
        }

        public void Update(CanTransmitData? frame = null, TimeSpan? period = null, int? repeatCount = null)
        {
            lock (_gate)
            {
                if (frame is not null) _frame = frame.Value;
                if (period.HasValue && period.Value > TimeSpan.Zero)
                {
                    if (period.Value < TimeSpan.FromMilliseconds(2))
                    {
                        //TODO:输出警告信息，太短的间隔时间不稳定
                    }
                    _period = period.Value;
                }
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

        private void Loop()
        {
            TryRaiseThreadPriority();

            var token = _cts.Token;
            var sw = Stopwatch.StartNew();

            _sInit(ref _ctx);
            _ctx.t0 = sw.Elapsed;  // Stopwatch 基准
            _ctx.lastPeriodApplied = TimeSpan.Zero;
            _sUpdatePolicy(ref _ctx, _period); // 初次设置策略

            var t0 = _ctx.t0;
            long n = 1;

            if (_fireImmediately)
            {
                TrySendOnce();
                if (DecreaseAndMaybeFinish()) { _running = false; return; }
                t0 = sw.Elapsed;
                _ctx.t0 = t0;
                _sResetAnchor(ref _ctx);
                n = 1;
            }

            while (!token.IsCancellationRequested)
            {
                TimeSpan period;
                lock (_gate) period = _period;
                if (period != _ctx.lastPeriodApplied)
                {
                    _sUpdatePolicy(ref _ctx, period);
                }


                var target = t0 + TimeSpan.FromTicks(period.Ticks * n);

                _sPreWait(ref _ctx, sw, target, token);

                // 发送
                var sendStart = sw.Elapsed;
                TrySendOnce();

                RecordJitter(sendStart - target);

                if (DecreaseAndMaybeFinish()) break;

                // 跳过落拍，保持绝对对齐
                n = Math.Max(n + 1, (long)Math.Floor((sw.Elapsed - t0).Ticks / (double)period.Ticks) + 1);
            }

            _running = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrySendOnce()
        {
            try
            {
                lock (_gate)
                {
                    _txBuf[0] = _frame;
                    _ = _bus.Transmit(_txBuf);
                }
            }
            catch { /*ignore*/ }
        }

        private bool DecreaseAndMaybeFinish()
        {
            lock (_gate)
            {
                if (_remaining == 0) return false;
                if (_remaining > 0)
                {
                    _remaining--;
                    if (_remaining == 0)
                    {
                        try { Completed?.Invoke(this, EventArgs.Empty); } catch { /*Ignore*/ }
                        Stop();
                        return true;
                    }
                }
            }
            return false;
        }

        // ========= 抖动统计 =========
        private void RecordJitter(TimeSpan delta)
        {
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
            try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { /*Ignore*/ }
        }

        // ========= 策略分级 =========
        private enum PrecisionTier : byte { Ultra, Tight, Normal, Coarse }

        private struct TimerPolicy
        {
            public PrecisionTier Tier;
            public double GuardMs;        // 预等余量（Windows）
            public int SpinBudgetUs;      // 尾段最多自旋
            public int FineYieldUs;       // Yield 阈值
            public uint TolerableDelayMs; // Windows 容差
            public bool UseThreadSleep;   // 粗粒度直接 Sleep
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimerPolicy BuildPolicy(TimeSpan period)
        {
            // 你可按需要调参（此处默认：Ultra≤2ms, Tight≤10ms, Normal≤50ms, Coarse>50ms）
            var ms = period.TotalMilliseconds;

            if (ms <= 4)
            {
                return new TimerPolicy
                {
                    Tier = PrecisionTier.Ultra,
                    GuardMs = 0.08,           // 80 µs
                    SpinBudgetUs = 30,        // ≤30 µs 自旋
                    FineYieldUs = 1500,       // >1.5 ms 才 yield
                    TolerableDelayMs = 0,     // 尽量准
                    UseThreadSleep = false
                };
            }
            if (ms <= 15.0)
            {
                return new TimerPolicy
                {
                    Tier = PrecisionTier.Tight,
                    GuardMs = 0.20,           // ~0.2 ms
                    SpinBudgetUs = 15,        // 极小自旋
                    FineYieldUs = 2000,       // >2 ms 才 yield
                    TolerableDelayMs = (uint)Math.Min(ms * 0.25, 2.0), // 给内核一点合并空间
                    UseThreadSleep = false
                };
            }
            if (ms <= 50.0)
            {
                return new TimerPolicy
                {
                    Tier = PrecisionTier.Normal,
                    GuardMs = 0.60,           // 0.6 ms
                    SpinBudgetUs = 0,         // 不自旋
                    FineYieldUs = 5000,       // >5 ms yield
                    TolerableDelayMs = (uint)Math.Min(ms * 0.3, 5.0),
                    UseThreadSleep = false
                };
            }
            // Coarse：更省电，直接 Thread.Sleep + 末段轻让出
            return new TimerPolicy
            {
                Tier = PrecisionTier.Coarse,
                GuardMs = 1.00,              // 1 ms
                SpinBudgetUs = 0,
                FineYieldUs = 8000,
                TolerableDelayMs = (uint)Math.Min(ms * 0.4, 10.0),
                UseThreadSleep = true
            };
        }

        // ========= 平台分派（静态绑定，热路径零分支） =========
        private struct PlatformContext
        {
            public TimeSpan t0;                 // Stopwatch 基准
            public nint hTimer;                 // Windows: waitable timer 句柄
            public Posix.timespec t0Mono;       // Linux: CLOCK_MONOTONIC 基准
            public TimerPolicy policy;          // 当前策略
            public TimeSpan lastPeriodApplied;  // 已应用的周期
            public bool winResAcquired;         // Windows: 是否已 Acquire(1)
        }

        private delegate void InitDelegate(ref PlatformContext ctx);
        private delegate void DisposeDelegate(ref PlatformContext ctx);
        private delegate void UpdatePolicyDelegate(ref PlatformContext ctx, TimeSpan period);
        private delegate void PreWaitDelegate(ref PlatformContext ctx, Stopwatch sw, TimeSpan target, CancellationToken token);
        private delegate void ResetAnchorDelegate(ref PlatformContext ctx);

        private static readonly bool _sIsWindows = IsWindows();
        private static readonly InitDelegate _sInit = _sIsWindows ? Win_Init : Posix_Init;
        private static readonly DisposeDelegate _sDispose = _sIsWindows ? Win_Dispose : Posix_Dispose;
        private static readonly UpdatePolicyDelegate _sUpdatePolicy = _sIsWindows ? Win_UpdatePolicy : Posix_UpdatePolicy;
        private static readonly PreWaitDelegate _sPreWait = _sIsWindows ? Win_PreWait : Posix_PreWait;
        private static readonly ResetAnchorDelegate _sResetAnchor =
            _sIsWindows ? ((ref PlatformContext _) => { }) : Posix_ResetAnchor;

        private static bool IsWindows()
        {
#if NET5_0_OR_GREATER
            return OperatingSystem.IsWindows();
#else
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
        }

        // ===== Windows 实现 =====
        private static void Win_Init(ref PlatformContext ctx)
        {
            // 句柄先创建；是否 Acquire(1) 由策略决定
            ctx.hTimer = Win_CreateHiResTimer();
        }

        private static void Win_Dispose(ref PlatformContext ctx)
        {
            if (ctx.winResAcquired)
            {
                try { WinTimerResolution.Release(); } catch { }
                ctx.winResAcquired = false;
            }

            if (ctx.hTimer != 0)
            {
                try { Win32.CloseHandle(ctx.hTimer); } catch { /*Ignore*/ }
                ctx.hTimer = 0;
            }
        }

        private static void Win_UpdatePolicy(ref PlatformContext ctx, TimeSpan period)
        {
            var newPolicy = BuildPolicy(period);

            // Ultra 档才打开 1ms 全局分辨率；其它档关闭
            if (newPolicy.Tier == PrecisionTier.Ultra)
            {
                if (!ctx.winResAcquired)
                {
                    try { WinTimerResolution.Acquire(1); ctx.winResAcquired = true; } catch { }
                }
            }
            else
            {
                if (ctx.winResAcquired)
                {
                    try { WinTimerResolution.Release(); ctx.winResAcquired = false; } catch { }
                }
            }

            ctx.policy = newPolicy;
            ctx.lastPeriodApplied = period;
        }

        private static void Win_PreWait(ref PlatformContext ctx, Stopwatch sw, TimeSpan target, CancellationToken token)
        {
            var policy = ctx.policy;

            if (policy.UseThreadSleep)
            {
                SleepCoarse(sw, target, token, policy.GuardMs);
                return;
            }


            while (!token.IsCancellationRequested)
            {
                var remain = target - sw.Elapsed;
                if (remain <= TimeSpan.Zero) break;

                var remainMs = remain.TotalMilliseconds;
                var remainUs = remainMs * 1000.0;

                if (remainMs > policy.GuardMs + 0.1)
                {
                    var wait = TimeSpan.FromMilliseconds(remainMs - policy.GuardMs);
                    Win_WaitRelativeHiRes(ctx.hTimer, wait, policy.TolerableDelayMs);
                }
                else if (remainUs > policy.FineYieldUs)
                {
                    Thread.Yield();
                }
                else if (policy.SpinBudgetUs > 0 && remainUs > policy.SpinBudgetUs)
                {
                    Thread.SpinWait(64);
                }
                else
                {
                    if (policy.SpinBudgetUs > 0)
                    {
                        var spinUntil = target - FromMicroseconds(policy.SpinBudgetUs);
                        while (!token.IsCancellationRequested && sw.Elapsed < target && sw.Elapsed < spinUntil)
                            Thread.SpinWait(16);
                    }
                    break;
                }
            }
        }

        private static void Win_WaitRelativeHiRes(nint hTimer, TimeSpan due, uint tolerableDelayMs)
        {
            if (due <= TimeSpan.Zero) return;

            if (hTimer != 0)
            {
                long due100Ns = -(long)(due.TotalMilliseconds * 10_000.0);
                if (!Win32.SetWaitableTimerEx(hTimer, ref due100Ns, 0, 0, 0, 0, tolerableDelayMs))
                {
                    Thread.Sleep(due); // 退化
                    return;
                }
                Win32.WaitForSingleObject(hTimer, Win32.INFINITE);
            }
            else
            {
                Thread.Sleep(due);
            }
        }

        private static nint Win_CreateHiResTimer()
        {
            try
            {
                var h = Win32.CreateWaitableTimerEx(0, null,
                    Win32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Win32.TIMER_ALL_ACCESS);
                if (h != 0) return h;
                return Win32.CreateWaitableTimerEx(0, null, 0, Win32.TIMER_ALL_ACCESS);
            }
            catch { return 0; }
        }


        private static class WinTimerResolution
        {
            private static int _sRef;
            private static uint _sMs;

            public static void Acquire(uint ms)
            {
                if (Interlocked.Increment(ref _sRef) == 1)
                {
                    _sMs = ms;
                    try { Win32.timeBeginPeriod(ms); } catch { }
                }
            }

            public static void Release()
            {
                if (Interlocked.Decrement(ref _sRef) == 0)
                {
                    try { Win32.timeEndPeriod(_sMs); } catch { }
                    _sMs = 0;
                }
            }
        }

        private static class Win32
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern nint CreateWaitableTimerEx(nint lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetWaitableTimerEx(nint hTimer, ref long pDueTime100ns, int periodMs,
                nint pfnCompletionRoutine, nint lpArg, nint wakeContext, uint tolerableDelayMs);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint WaitForSingleObject(nint hHandle, uint milliseconds);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CloseHandle(nint hObject);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint timeBeginPeriod(uint ms);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint timeEndPeriod(uint ms);

            internal const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x1;
            internal const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2; // Win10+
            internal const uint TIMER_ALL_ACCESS = 0x001F0003;
            internal const uint INFINITE = 0xFFFFFFFF;
        }

        // ===== Linux 实现 =====
        private static void Posix_Init(ref PlatformContext ctx)
        {
            Posix.clock_gettime(Posix.CLOCK_MONOTONIC, out ctx.t0Mono);
        }

        private static void Posix_UpdatePolicy(ref PlatformContext ctx, TimeSpan period)
        {
            ctx.policy = BuildPolicy(period);
            ctx.lastPeriodApplied = period;
        }

        private static void Posix_ResetAnchor(ref PlatformContext ctx)
        {
            Posix.clock_gettime(Posix.CLOCK_MONOTONIC, out ctx.t0Mono);
        }

        private static void Posix_Dispose(ref PlatformContext ctx)
        {
            ctx.t0Mono = default;
        }

        private static void Posix_PreWait(ref PlatformContext ctx, Stopwatch sw, TimeSpan target, CancellationToken token)
        {
            var policy = ctx.policy;

            if (policy.UseThreadSleep)
            {
                SleepCoarse(sw, target, token, policy.GuardMs);
                return;
            }

            // 直接绝对睡到目标，末段仅做极小矫正（或不自旋）
            var deltaFromT0 = target - ctx.t0;
            var targetMono = Posix.Add(ctx.t0Mono, deltaFromT0);

            try { Posix.SleepUntilMonotonicAbs(targetMono); } catch { /*EINTR 可忽略*/ }

            // 如果早醒且非常接近目标，再做一次极短自旋
            if (policy.SpinBudgetUs > 0)
            {
                var remain = target - sw.Elapsed;
                var remainUs = remain.TotalMilliseconds * 1000.0;
                if (remain > TimeSpan.Zero && remainUs <= policy.SpinBudgetUs)
                {
                    var spinUntil = target - FromMicroseconds(policy.SpinBudgetUs);
                    while (!token.IsCancellationRequested && sw.Elapsed < target && sw.Elapsed < spinUntil)
                        Thread.SpinWait(16);
                }
            }
        }

#pragma warning disable IDE0055
#pragma warning disable CS8981
        internal static class Posix
        {
            internal const int CLOCK_MONOTONIC = 1;
            internal const int TIMER_ABSTIME = 1;

            [StructLayout(LayoutKind.Sequential)]
            internal struct timespec { public long tv_sec; public long tv_nsec; }

            [DllImport("libc", SetLastError = true)]
            internal static extern int clock_gettime(int clk_id, out timespec tp);

            [DllImport("libc", SetLastError = true)]
            private static extern int clock_nanosleep(int clk_id, int flags, in timespec request, nint remain);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static timespec Add(timespec a, TimeSpan delta)
            {
                long ns = (long)(delta.TotalMilliseconds * 1_000_000.0);
                long sec = ns / 1_000_000_000; ns %= 1_000_000_000;
                var r = new timespec { tv_sec = a.tv_sec + sec, tv_nsec = a.tv_nsec + ns };
                if (r.tv_nsec >= 1_000_000_000) { r.tv_sec++; r.tv_nsec -= 1_000_000_000; }
                return r;
            }

            internal static void SleepUntilMonotonicAbs(timespec absTarget)
            {
                int rc = clock_nanosleep(CLOCK_MONOTONIC, TIMER_ABSTIME, absTarget, 0);
                if (rc != 0 && rc != 4 /* EINTR */)
                    throw new System.ComponentModel.Win32Exception(rc);
            }
        }
#pragma warning restore IDE0055
#pragma warning restore CS8981

        // ========= 共享小工具 =========
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TimeSpan FromMicroseconds(int us) => TimeSpan.FromTicks(us * 10L);

        // 粗粒度睡眠：先 Sleep 到 target-guard，再轻让出
        private static void SleepCoarse(Stopwatch sw, TimeSpan target, CancellationToken token, double guardMs)
        {
            while (!token.IsCancellationRequested)
            {
                var remain = target - sw.Elapsed;
                if (remain <= TimeSpan.Zero) break;

                if (remain.TotalMilliseconds > guardMs + 1.0)
                {
                    var ms = remain.TotalMilliseconds - guardMs;
                    // Thread.Sleep 只能到毫秒，做个保守下取整
                    int sleepMs = (int)Math.Max(1, Math.Floor(ms));
                    Thread.Sleep(sleepMs);
                }
                else if (remain.TotalMilliseconds > 1.5)
                {
                    Thread.Yield();
                }
                else
                {
                    // 最后阶段尽量不用自旋（Coarse/Normal 一般为 0）
                    Thread.SpinWait(64);
                    break;
                }
            }
        }
    }

    // ========= 小工具 =========
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
