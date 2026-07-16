#if !NET6_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Test-only polyfill for the .NET 6+ <c>Task.WaitAsync(TimeSpan)</c> family. Multiple test
    /// files use <c>WaitAsync</c> to prevent hangs; the polyfill lets those same call sites
    /// compile on the net48 CI leg without changing test source.
    /// </summary>
    internal static class TaskWaitAsyncPolyfill
    {
        public static Task<TResult> WaitAsync<TResult>(this Task<TResult> task, TimeSpan timeout)
            => WaitAsync(task, timeout, CancellationToken.None);

        public static Task<TResult> WaitAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
            => WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);

        public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            if (task.IsCompleted) return await task.ConfigureAwait(false);

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = timeout == Timeout.InfiniteTimeSpan
                ? Task.Delay(-1, delayCts.Token)
                : Task.Delay(timeout, delayCts.Token);

            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == task)
            {
                delayCts.Cancel();
                return await task.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Task did not complete within {timeout}.");
        }

        public static Task WaitAsync(this Task task, TimeSpan timeout)
            => WaitAsync(task, timeout, CancellationToken.None);

        public static Task WaitAsync(this Task task, CancellationToken cancellationToken)
            => WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);

        public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (task is null) throw new ArgumentNullException(nameof(task));
            if (task.IsCompleted) { await task.ConfigureAwait(false); return; }

            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = timeout == Timeout.InfiniteTimeSpan
                ? Task.Delay(-1, delayCts.Token)
                : Task.Delay(timeout, delayCts.Token);

            var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            if (completed == task)
            {
                delayCts.Cancel();
                await task.ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Task did not complete within {timeout}.");
        }
    }
}
#endif
