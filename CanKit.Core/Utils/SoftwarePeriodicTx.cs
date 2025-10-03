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
    public sealed class SoftwarePeriodicTx : IPeriodicTx
    {
        private readonly ICanBus _bus;
        private readonly CancellationTokenSource _cts = new();
        private readonly bool _fireImmediately;
        private readonly object _gate = new();

        private CanTransmitData _frame;

        private long _jitterMin, _jitterMax, _jitterLastNs, _jitterSumNs, _jitterCount;
        private TimeSpan _period;
        private int _remaining; // -1 = 无限
        private int _repeat;
        private volatile bool _running;
        private Task? _task;

        private PlatformContext _ctx;

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
                // 避免在循环线程里自等导致死锁
                if (_task != null && Task.CurrentId != _task.Id)
                    _task.Wait(200);
            }
            catch { /*ignore exception when stop*/ }
            finally
            {
                _running = false;
                // 释放平台资源
                _sDispose(ref _ctx);
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

        private void Loop()
        {
            TryRaiseThreadPriority();

            var token = _cts.Token;
            var sw = Stopwatch.StartNew();


            _sInit(ref _ctx);

            // 将当前 Stopwatch 的 t0 记录进上下文，用于 Linux 折算到 CLOCK_MONOTONIC
            _ctx.t0 = sw.Elapsed;

            var t0 = _ctx.t0;
            long n = 1;

            if (_fireImmediately)
            {
                TrySendOnce();
                if (DecreaseAndMaybeFinish()) { _running = false; return; }
                // 即发：把起点移到现在，同时让平台更新它的锚点（Linux 需更新 MONOTONIC 基准）
                t0 = sw.Elapsed;
                _ctx.t0 = t0;
                _sResetAnchor(ref _ctx);
                n = 1;
            }

            while (!token.IsCancellationRequested)
            {
                // 动态 period
                TimeSpan period;
                lock (_gate) period = _period;

                // 绝对对齐：t0 + n*period
                var target = t0 + TimeSpan.FromTicks(period.Ticks * n);

                // 平台化的“到预自旋点”的等待 + 末段让出/自旋（热路径零分支）
                _sPreWait(ref _ctx, sw, target, token);

                // 发送
                var sendStart = sw.Elapsed;
                TrySendOnce();
                //var sendEnd = sw.Elapsed;

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
                CanTransmitData frame;
                lock (_gate) { frame = _frame; }
                _ = _bus.Transmit(new[] { frame });
            }
            catch { /* 后台异常不炸循环 */ }
        }

        private bool DecreaseAndMaybeFinish()
        {
            lock (_gate)
            {
                if (_remaining == 0)
                    return false;
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

        // ========= 平台分派（静态绑定，热路径零分支） =========

        // 平台上下文（实例级）：Windows 用 HTimer；Linux 用 T0Mono
        private struct PlatformContext
        {
            public TimeSpan t0;                 // Stopwatch 基准（两平台通用）
            public nint hTimer;                 // Windows: waitable timer 句柄
            public Posix.timespec t0Mono;       // Linux: CLOCK_MONOTONIC 基准
        }

        private delegate void InitDelegate(ref PlatformContext ctx);
        private delegate void DisposeDelegate(ref PlatformContext ctx);
        private delegate void PreWaitDelegate(ref PlatformContext ctx, Stopwatch sw, TimeSpan target, CancellationToken token);
        private delegate void ResetAnchorDelegate(ref PlatformContext ctx);

        private static readonly bool _sIsWindows = IsWindows();
        private static readonly InitDelegate _sInit = _sIsWindows ? Win_Init : Posix_Init;
        private static readonly DisposeDelegate _sDispose = _sIsWindows ? Win_Dispose : Posix_Dispose;
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
            // 引用计数化 timeBeginPeriod(1)（避免多实例反复开关）
            WinTimerResolution.Acquire(1);

            // Create Waitable Timer（优先高精度）
            ctx.hTimer = Win_CreateHiResTimer();
        }

        private static void Win_Dispose(ref PlatformContext ctx)
        {
            if (ctx.hTimer != 0)
            {
                try { Win32.CloseHandle(ctx.hTimer); } catch { /*Ignore*/ }
                ctx.hTimer = 0;
            }
            WinTimerResolution.Release();
        }

        private static void Win_PreWait(ref PlatformContext ctx, Stopwatch sw, TimeSpan target, CancellationToken token)
        {
            // 策略：> (1ms + 0.5ms) 用 WaitableTimer 预等到 target-1ms；随后让出/自旋
            const double guardMs = 1.0;      // 自旋前保留 1ms 余量
            const int fineYieldUs = 1000;    // >1ms 让出
            const int spinUs = 150;          // >150µs 轻量自旋

            while (!token.IsCancellationRequested)
            {
                var remain = target - sw.Elapsed;
                if (remain <= TimeSpan.Zero) break;

                var remainMs = remain.TotalMilliseconds;
                var remainUs = remainMs * 1000.0;

                if (remainMs > guardMs + 0.5)
                {
                    var wait = TimeSpan.FromMilliseconds(remainMs - guardMs);
                    Win_WaitRelativeHiRes(ctx.hTimer, wait);
                }
                else if (remainUs > fineYieldUs)
                {
                    Thread.Yield();
                }
                else if (remainUs > spinUs)
                {
                    Thread.SpinWait(128);
                }
                else
                {
                    while (!token.IsCancellationRequested && sw.Elapsed < target)
                        Thread.SpinWait(16);
                    break;
                }
            }
        }

        private static void Win_WaitRelativeHiRes(nint hTimer, TimeSpan due)
        {
            if (due <= TimeSpan.Zero) return;

            if (hTimer != 0)
            {
                long due100Ns = -(long)(due.TotalMilliseconds * 10_000.0); // 负数=相对时间
                if (!Win32.SetWaitableTimerEx(hTimer, ref due100Ns, 0, 0, 0, 0, 0))
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

        // 进程级 timeBeginPeriod(1) 引用计数
        private static class WinTimerResolution
        {
            private static int _sRef;
            private static uint _sMs;

            public static void Acquire(uint ms)
            {
                if (Interlocked.Increment(ref _sRef) == 1)
                {
                    _sMs = ms;
                    try { Win32.timeBeginPeriod(ms); } catch {/*Ignore*/ }
                }
            }

            public static void Release()
            {
                if (Interlocked.Decrement(ref _sRef) == 0)
                {
                    try { Win32.timeEndPeriod(_sMs); } catch {/*Ignore*/  }
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
            // 先睡到 target-150us（单调时钟），再细让出/自旋到点
            const long spinGuardNs = 150_000; // 150µs
            const int fineYieldUs = 1000;     // 1ms
            const int spinUs = 150;           // 150µs

            // 折算出 CLOCK_MONOTONIC 的绝对目标时刻
            var deltaFromT0 = target - ctx.t0;
            var targetMono = Posix.Add(ctx.t0Mono, deltaFromT0);
            var preSpin = Posix.Subtract(targetMono, spinGuardNs);

            try { Posix.SleepUntilMonotonicAbs(preSpin); } catch { /* EINTR 可忽略 */ }

            // 末段让出/自旋（以 Stopwatch 为准，避免多次 P/Invoke）
            while (!token.IsCancellationRequested)
            {
                var remain = target - sw.Elapsed;
                if (remain <= TimeSpan.Zero) break;

                var remainUs = remain.TotalMilliseconds * 1000.0;
                if (remainUs > fineYieldUs)
                    Thread.Yield();
                else if (remainUs > spinUs)
                    Thread.SpinWait(128);
                else
                {
                    while (!token.IsCancellationRequested && sw.Elapsed < target)
                        Thread.SpinWait(16);
                    break;
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static timespec Subtract(timespec a, long deltaNs)
            {
                long ns = a.tv_nsec - deltaNs;
                long sec = a.tv_sec;
                while (ns < 0) { ns += 1_000_000_000; sec--; }
                return new timespec { tv_sec = sec, tv_nsec = ns };
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
