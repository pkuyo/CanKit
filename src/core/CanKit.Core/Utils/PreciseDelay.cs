using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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


    private static class WindowsHighResWaitableTimer
    {
        private const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x001F0003;
        private const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerEx(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimerEx(IntPtr hTimer, in long lpDueTime, int lPeriod,
                                                      IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine,
                                                      IntPtr wakeContext, uint tolerableDelay /* 100ns 单位 */);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public static void SleepMs(double ms, CancellationToken ct)
        {
            // 负值表示相对时间（100ns 单位）
            long due100ns = -(long)(ms * 10_000.0);

            IntPtr h = CreateWaitableTimerEx(IntPtr.Zero, null,
                                             CREATE_WAITABLE_TIMER_HIGH_RESOLUTION | CREATE_WAITABLE_TIMER_MANUAL_RESET,
                                             TIMER_ALL_ACCESS);
            if (h == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                if (!SetWaitableTimerEx(h, in due100ns, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                // 轮询取消（Windows WaitForSingleObject 无法直接用 CancellationToken）
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    const uint SLICE_MS = 10;
                    var result = WaitForSingleObject(h, SLICE_MS);
                    if (result == 0 /* WAIT_OBJECT_0 */) break;
                    if (result != 0x00000102 /* WAIT_TIMEOUT */)
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                CloseHandle(h);
            }
        }
    }

    private sealed class TimePeriodScope : IDisposable
    {
        private readonly uint _ms;
        public TimePeriodScope(uint milliseconds)
        {
            _ms = milliseconds;
            try { timeBeginPeriod(_ms); } catch { /* 忽略失败 */ }
        }
        public void Dispose()
        {
            try { timeEndPeriod(_ms); } catch { /* 忽略失败 */ }
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint timeEndPeriod(uint uMilliseconds);
    }
}
