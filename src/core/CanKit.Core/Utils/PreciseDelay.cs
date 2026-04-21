using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CanKit.Core.Utils;

public static class PreciseDelay
{
    public static void Delay(TimeSpan delay,
                             bool onWindowsUseTimeBeginPeriod = false,
                             double spinBackoffMs = 0.8,
                             CancellationToken ct = default)
    {
        if (delay <= TimeSpan.Zero) return;

        var start = Stopwatch.GetTimestamp();
        var freq = Stopwatch.Frequency;
        var target = start + (long)(delay.TotalSeconds * freq);

        var coarseMs = Math.Max(0, delay.TotalMilliseconds - Math.Max(0.1, spinBackoffMs));
        if (coarseMs >= 0.5)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var _ = onWindowsUseTimeBeginPeriod ? new TimePeriodScope(1) : null;
                WindowsHighResWaitableTimer.SleepMs(coarseMs, ct);
            }
            else
            {
                Task.Delay(TimeSpan.FromMilliseconds(coarseMs), ct).GetAwaiter().GetResult();
            }
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long now = Stopwatch.GetTimestamp();
            if (now >= target) break;

            double remainingUs = (target - now) * 1_000_000.0 / freq;
            if (remainingUs > 150)
            {
                Thread.SpinWait(200);
            }
            else
            {
                while (Stopwatch.GetTimestamp() < target) { }
                break;
            }
        }
    }

    public static async Task DelayAsync(TimeSpan delay,
        bool onWindowsUseTimeBeginPeriod = false,
        double spinBackoffMs = 0.8,
        CancellationToken ct = default)
    {
        if (delay <= TimeSpan.Zero) return;

        var start = Stopwatch.GetTimestamp();
        var freq = Stopwatch.Frequency;
        var target = start + (long)(delay.TotalSeconds * freq);

        var coarseMs = Math.Max(0, delay.TotalMilliseconds - Math.Max(0.1, spinBackoffMs));
        if (coarseMs >= 0.5)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var _ = onWindowsUseTimeBeginPeriod ? new TimePeriodScope(1) : null;
                await WindowsHighResWaitableTimer.SleepMsAsync(coarseMs, ct);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(coarseMs), ct);
            }
        }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long now = Stopwatch.GetTimestamp();
            if (now >= target) break;

            double remainingUs = (target - now) * 1_000_000.0 / freq;
            if (remainingUs > 150)
            {
                Thread.SpinWait(200);
            }
            else
            {
                while (Stopwatch.GetTimestamp() < target) { }
                break;
            }
        }
    }


    private static class WindowsHighResWaitableTimer
    {
        private const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;

        private const uint TIMER_MODIFY_STATE = 0x0002;
        private const uint SYNCHRONIZE = 0x00100000;

        // 等待句柄需要 SYNCHRONIZE，SetWaitableTimerEx 需要 TIMER_MODIFY_STATE
        private const uint TIMER_ACCESS = TIMER_MODIFY_STATE | SYNCHRONIZE;

        private const int ERROR_INVALID_PARAMETER = 87;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeWaitHandle CreateWaitableTimerEx(
            IntPtr lpTimerAttributes,
            string lpTimerName,
            uint dwFlags,
            uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimerEx(
            SafeWaitHandle hTimer,
            ref long lpDueTime,
            int lPeriod,
            IntPtr pfnCompletionRoutine,
            IntPtr lpArgToCompletionRoutine,
            IntPtr wakeContext,
            uint tolerableDelayMilliseconds);

        public static void SleepMs(double ms, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (ms <= 0)
                return;

            using ManualResetEvent wh = CreateOneShotTimer(ms);

            if (!ct.CanBeCanceled)
            {
                wh.WaitOne();
                return;
            }

            int index = WaitHandle.WaitAny(new WaitHandle[]
            {
            wh,
            ct.WaitHandle
            });

            if (index == 1)
                throw new OperationCanceledException(ct);
        }

        public static Task SleepMsAsync(double ms, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return Task.FromCanceled(ct);

            if (ms <= 0)
                return Task.CompletedTask;

            ManualResetEvent wh;

            try
            {
                wh = CreateOneShotTimer(ms);
            }
            catch
            {
                throw;
            }

            var tcs = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            RegisteredWaitHandle? rwh = null;
            CancellationTokenRegistration ctr = default;

            int completed = 0;

            void CompleteSuccess()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0)
                    return;

                try { ctr.Dispose(); } catch { }
                try { rwh?.Unregister(null); } catch { }

                wh.Dispose();
                tcs.TrySetResult(null);
            }

            void CompleteCanceled()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0)
                    return;

                try { rwh?.Unregister(null); } catch { }

                wh.Dispose();
                tcs.TrySetCanceled(ct);
            }

            rwh = ThreadPool.RegisterWaitForSingleObject(
                wh,
                static (state, timedOut) => ((Action)state!).Invoke(),
                (Action)CompleteSuccess,
                Timeout.Infinite,
                executeOnlyOnce: true);

            // 防止 timer 极短时，回调可能在 rwh 赋值附近已经完成
            if (Volatile.Read(ref completed) != 0)
            {
                try { rwh.Unregister(null); } catch { }
                return tcs.Task;
            }

            if (ct.CanBeCanceled)
            {
                ctr = ct.Register(
                    static state => ((Action)state!).Invoke(),
                    (Action)CompleteCanceled);

                // 防止注册取消回调时，timer 已经完成
                if (Volatile.Read(ref completed) != 0)
                {
                    try { ctr.Dispose(); } catch { }
                }
            }

            return tcs.Task;
        }

        private static ManualResetEvent CreateOneShotTimer(double ms)
        {
            if (double.IsNaN(ms) || double.IsInfinity(ms))
                throw new ArgumentOutOfRangeException(nameof(ms));

            if (ms <= 0)
            {
                var alreadySet = new ManualResetEvent(true);
                return alreadySet;
            }

            long due100ns = ToRelativeDueTime100ns(ms);

            SafeWaitHandle handle = CreateWaitableTimerWithFallback();

            var wh = new ManualResetEvent(false)
            {
                SafeWaitHandle = handle
            };

            bool ok = SetWaitableTimerEx(
                handle,
                ref due100ns,
                lPeriod: 0,
                pfnCompletionRoutine: IntPtr.Zero,
                lpArgToCompletionRoutine: IntPtr.Zero,
                wakeContext: IntPtr.Zero,
                tolerableDelayMilliseconds: 0);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                wh.Dispose();
                throw new Win32Exception(err);
            }

            return wh;
        }

        private static SafeWaitHandle CreateWaitableTimerWithFallback()
        {
            uint highResFlags =
                CREATE_WAITABLE_TIMER_MANUAL_RESET |
                CREATE_WAITABLE_TIMER_HIGH_RESOLUTION;

            SafeWaitHandle handle = CreateWaitableTimerEx(
                IntPtr.Zero,
                null!,
                highResFlags,
                TIMER_ACCESS);

            if (!handle.IsInvalid)
                return handle;

            int err = Marshal.GetLastWin32Error();
            handle.Dispose();

            // Windows 7 不支持 CREATE_WAITABLE_TIMER_HIGH_RESOLUTION，
            // 会返回 ERROR_INVALID_PARAMETER。
            if (err != ERROR_INVALID_PARAMETER)
                throw new Win32Exception(err);

            uint normalFlags = CREATE_WAITABLE_TIMER_MANUAL_RESET;

            handle = CreateWaitableTimerEx(
                IntPtr.Zero,
                null!,
                normalFlags,
                TIMER_ACCESS);

            if (!handle.IsInvalid)
                return handle;

            err = Marshal.GetLastWin32Error();
            handle.Dispose();

            throw new Win32Exception(err);
        }

        private static long ToRelativeDueTime100ns(double ms)
        {
            // WaitableTimer 的相对时间要求是负数，单位 100ns。
            // 1ms = 10,000 * 100ns。
            double value = Math.Ceiling(ms * 10_000.0);

            if (value > long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(ms));

            return -(long)value;
        }
    }

    private sealed class TimePeriodScope : IDisposable
    {
        private readonly uint _ms;
        public TimePeriodScope(uint milliseconds)
        {
            _ms = milliseconds;
            try { timeBeginPeriod(_ms); } catch { /*Ignored*/ }
        }
        public void Dispose()
        {
            try { timeEndPeriod(_ms); } catch {  /*Ignored*/ }
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint timeEndPeriod(uint uMilliseconds);
    }
}
