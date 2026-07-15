using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CanKit.Pro.Actor
{
    /// <summary>
    /// Default <see cref="IProtocolActor"/>: one mailbox (<see cref="ConcurrentQueue{T}"/> of work
    /// items), one loop, one sorted list of pending timers — both owned exclusively by whichever
    /// thread is currently running the loop, so neither needs its own lock (FR-RAW-020/021). The
    /// loop blocks on a <see cref="SemaphoreSlim"/> for either new mailbox work or the next timer
    /// deadline, whichever comes first, and never polls (FR-RAW-022). Any exception from a posted
    /// work item or a fired timer is caught and raised via
    /// <see cref="BackgroundExceptionOccurred"/>; the loop keeps running afterward (FR-RAW-023).
    /// </summary>
    public sealed class ProtocolActor : IProtocolActor
    {
        private readonly ConcurrentQueue<Action> _mailbox = new();

        // Released once per Post/Schedule call; the loop waits on it (blocking or async depending
        // on execution mode) instead of polling. Over-counting is harmless: each wake drains
        // *everything* currently available, not just one item, so an extra pending count just
        // causes one additional, cheap, empty-ish iteration.
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

        // Sorted ascending by TimerEntry.DueUtc. Touched only by the loop thread (both when
        // draining Schedule's "insert" messages from the mailbox and when firing due timers), so
        // it needs no lock of its own -- the same single-writer guarantee FR-RAW-021 asks every
        // protocol instance to have for its own state.
        private readonly List<TimerEntry> _timers = new();

        private readonly CancellationTokenSource _stopCts = new();
        private readonly SynchronizationContext? _syncContext;
        private readonly Thread? _dedicatedThread;
        private readonly Task? _loopTask;
        private int _disposedFlag;

        /// <inheritdoc />
        public event EventHandler<Exception>? BackgroundExceptionOccurred;

        /// <summary>
        /// Creates an actor and immediately starts its mailbox loop under
        /// <paramref name="mode"/>. (创建 Actor 并立即以 <paramref name="mode"/> 启动其邮箱循环。)
        /// </summary>
        /// <param name="mode">Execution context for the loop (FR-RAW-024). Defaults to a dedicated thread.</param>
        /// <param name="synchronizationContext">
        /// Required when <paramref name="mode"/> is <see cref="ActorExecutionMode.SynchronizationContext"/>;
        /// must be null for every other mode.
        /// </param>
        public ProtocolActor(ActorExecutionMode mode = ActorExecutionMode.DedicatedThread, SynchronizationContext? synchronizationContext = null)
        {
            if (mode == ActorExecutionMode.SynchronizationContext)
            {
                _syncContext = synchronizationContext
                    ?? throw new ArgumentNullException(nameof(synchronizationContext), $"{nameof(ActorExecutionMode.SynchronizationContext)} mode requires a non-null context.");
            }
            else if (synchronizationContext is not null)
            {
                throw new ArgumentException($"{nameof(synchronizationContext)} is only used with {nameof(ActorExecutionMode.SynchronizationContext)} mode.", nameof(synchronizationContext));
            }

            if (mode == ActorExecutionMode.DedicatedThread)
            {
                // A genuine System.Threading.Thread, not an async Task with LongRunning: only a
                // real dedicated thread guarantees every iteration -- across every await-equivalent
                // wait point -- keeps running on that exact same thread (FR-RAW-024's verification
                // criterion). An async loop resumed via the thread pool after a wait has no such
                // guarantee, since nothing marshals its continuation back to one specific thread.
                _dedicatedThread = new Thread(RunLoopBlocking) { IsBackground = true, Name = "CanKit.Pro.Actor" };
                _dedicatedThread.Start();
            }
            else
            {
                _loopTask = Task.Run(RunLoopAsync);
            }
        }

        /// <inheritdoc />
        public void Post(Action work)
        {
            if (work is null) throw new ArgumentNullException(nameof(work));
            ThrowIfDisposed();
            _mailbox.Enqueue(work);
            _signal.Release();
        }

        /// <inheritdoc />
        public Task PostAsync(Action work)
        {
            if (work is null) throw new ArgumentNullException(nameof(work));
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try
                {
                    work();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <inheritdoc />
        public Task<T> PostAsync<T>(Func<T> work)
        {
            if (work is null) throw new ArgumentNullException(nameof(work));
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(() =>
            {
                try
                {
                    tcs.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        /// <inheritdoc />
        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            if (callback is null) throw new ArgumentNullException(nameof(callback));
            if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay), "Delay must not be negative.");
            ThrowIfDisposed();

            var entry = new TimerEntry(DateTime.UtcNow + delay, callback);
            // Inserted by the loop itself (as a mailbox message) so _timers is only ever touched
            // by the loop thread -- consistent with every other loop-owned-state rule here.
            Post(() => InsertTimerSorted(entry));
            return new TimerHandle(entry);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return; // idempotent

            // Wakes a blocked wait immediately; further Post/Schedule calls now throw
            // ObjectDisposedException instead of silently queuing work nobody will run.
            _stopCts.Cancel();

            if (_dedicatedThread is not null)
                _dedicatedThread.Join(TimeSpan.FromSeconds(5));
            else
            {
                try { _loopTask?.Wait(TimeSpan.FromSeconds(5)); }
                catch (AggregateException) { /* expected: the loop observes cancellation and exits via OperationCanceledException */ }
            }

            _stopCts.Dispose();
            _signal.Dispose();
        }

        private void RunLoopBlocking()
        {
            try
            {
                while (true)
                {
                    if (_stopCts.IsCancellationRequested) break;

                    var timeoutMs = NextWaitTimeoutMilliseconds();
                    try
                    {
                        _signal.Wait(timeoutMs, _stopCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    DrainMailbox();
                    FireDueTimers();
                }
            }
            finally
            {
                FinalDrain();
            }
        }

        private async Task RunLoopAsync()
        {
            try
            {
                while (true)
                {
                    if (_stopCts.IsCancellationRequested) break;

                    var timeoutMs = NextWaitTimeoutMilliseconds();
                    try
                    {
                        await _signal.WaitAsync(timeoutMs, _stopCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    DrainMailbox();
                    FireDueTimers();
                }
            }
            finally
            {
                FinalDrain();
            }
        }

        // Dispose semantics: stop accepting new work (Post/Schedule throw ObjectDisposedException
        // from that point on), but run everything already queued at the moment of Dispose to
        // completion -- so a caller awaiting PostAsync exactly when Dispose happens still gets a
        // real result/exception instead of a task that hangs forever. Never waits for or fires
        // not-yet-due timers; those are simply discarded.
        private void FinalDrain()
        {
            DrainMailbox();
            FireDueTimers();
        }

        private int NextWaitTimeoutMilliseconds()
        {
            while (_timers.Count > 0 && _timers[0].Cancelled)
                _timers.RemoveAt(0);

            if (_timers.Count == 0) return Timeout.Infinite;

            var remaining = _timers[0].DueUtc - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? 0 : ClampMilliseconds(remaining);
        }

        private void DrainMailbox()
        {
            while (_mailbox.TryDequeue(out var work))
                RunSafely(work);
        }

        private void FireDueTimers()
        {
            var now = DateTime.UtcNow;
            while (_timers.Count > 0 && _timers[0].DueUtc <= now)
            {
                var entry = _timers[0];
                _timers.RemoveAt(0);
                if (!entry.Cancelled)
                    RunSafely(entry.Callback);
            }
        }

        private void InsertTimerSorted(TimerEntry entry)
        {
            if (entry.Cancelled) return; // cancelled before the loop got around to inserting it

            var index = 0;
            while (index < _timers.Count && _timers[index].DueUtc <= entry.DueUtc)
                index++;
            _timers.Insert(index, entry);
        }

        private void RunSafely(Action work)
        {
            if (_syncContext is not null)
            {
                // FIFO-ordered by the target SynchronizationContext itself (true for standard UI
                // dispatchers): we don't wait for one Post to finish before issuing the next, but
                // the context still executes them one at a time in the order posted, preserving
                // single-writer semantics for actor-owned state.
                _syncContext.Post(state =>
                {
                    try { ((Action)state!)(); }
                    catch (Exception ex) { RaiseBackgroundException(ex); }
                }, work);
                return;
            }

            try
            {
                work();
            }
            catch (Exception ex)
            {
                RaiseBackgroundException(ex);
            }
        }

        private void RaiseBackgroundException(Exception ex)
        {
            try
            {
                BackgroundExceptionOccurred?.Invoke(this, ex);
            }
            catch
            {
                // A misbehaving subscriber must never be able to crash the actor loop itself.
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposedFlag) != 0)
                throw new ObjectDisposedException(nameof(ProtocolActor));
        }

        private static int ClampMilliseconds(TimeSpan span)
        {
            var ms = span.TotalMilliseconds;
            return ms >= int.MaxValue ? int.MaxValue : (int)ms;
        }

        private sealed class TimerEntry
        {
            public TimerEntry(DateTime dueUtc, Action callback)
            {
                DueUtc = dueUtc;
                Callback = callback;
            }

            public DateTime DueUtc { get; }
            public Action Callback { get; }
            public volatile bool Cancelled;
        }

        private sealed class TimerHandle : IDisposable
        {
            private readonly TimerEntry _entry;
            public TimerHandle(TimerEntry entry) => _entry = entry;
            public void Dispose() => _entry.Cancelled = true;
        }
    }
}
