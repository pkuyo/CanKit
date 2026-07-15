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

        // Schedule() insertions go through this queue instead of _mailbox, and are always applied
        // inline on the loop thread (never marshaled through _syncContext): _timers is
        // loop-thread-owned state, not user-facing work, so it must stay on the single logical
        // thread regardless of ActorExecutionMode. Routing it through _mailbox/RunSafely would
        // defer the insert onto the SynchronizationContext's own thread in that mode, letting it
        // race with FireDueTimers/NextWaitTimeoutMilliseconds on the loop thread, and -- since
        // nothing re-signals the loop once that deferred insert finally lands -- could also leave
        // a freshly scheduled timer sitting unnoticed behind whatever (possibly much longer or
        // infinite) wait the loop already committed to.
        private readonly ConcurrentQueue<TimerEntry> _pendingTimerInserts = new();

        // Released once per Post/Schedule call; the loop waits on it (blocking or async depending
        // on execution mode) instead of polling. Over-counting is harmless: each wake drains
        // *everything* currently available, not just one item, so an extra pending count just
        // causes one additional, cheap, empty-ish iteration.
        private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

        // Sorted ascending by TimerEntry.DueUtc. Touched only by the loop thread (both when
        // draining _pendingTimerInserts and when firing due timers), so it needs no lock of its
        // own -- the same single-writer guarantee FR-RAW-021 asks every protocol instance to have
        // for its own state.
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
            // Re-check after enqueueing: Dispose() could have run its entire final drain in the
            // gap between the check above and the enqueue, meaning nothing will ever look at
            // _mailbox again. We can't "unenqueue" from a ConcurrentQueue, so this can't fully
            // eliminate the race, but it closes the specific silent-failure case where Post would
            // otherwise appear to succeed while the work never runs -- the caller gets a clear
            // ObjectDisposedException instead.
            if (Volatile.Read(ref _disposedFlag) != 0)
                throw new ObjectDisposedException(nameof(ProtocolActor));
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
            // Applied by the loop itself, inline and never marshaled (see _pendingTimerInserts),
            // so _timers is only ever touched by the loop thread. _signal.Release() mirrors Post:
            // it wakes the loop promptly even if it's currently blocked waiting on a later,
            // unrelated timer deadline.
            _pendingTimerInserts.Enqueue(entry);
            // Same post-enqueue re-check as Post() -- see its comment for why this can narrow but
            // not fully eliminate a race against a concurrent Dispose().
            if (Volatile.Read(ref _disposedFlag) != 0)
                throw new ObjectDisposedException(nameof(ProtocolActor));
            _signal.Release();
            return new TimerHandle(entry);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return; // idempotent

            // Wakes a blocked wait immediately; further Post/Schedule calls now throw
            // ObjectDisposedException instead of silently queuing work nobody will run.
            _stopCts.Cancel();

            bool loopFinished;
            if (_dedicatedThread is not null)
                loopFinished = _dedicatedThread.Join(TimeSpan.FromSeconds(5));
            else
            {
                try { loopFinished = _loopTask is null || _loopTask.Wait(TimeSpan.FromSeconds(5)); }
                catch (AggregateException) { loopFinished = true; /* expected: the loop observed cancellation and exited via OperationCanceledException */ }
            }

            // Only dispose these if the loop actually finished: if some posted work item is still
            // blocking the loop past the grace period, it may still be running (e.g. stuck inside
            // a user callback) and could touch _signal/_stopCts.Token again once that callback
            // finally returns. Disposing out from under a still-running loop would surface as an
            // ObjectDisposedException on that thread -- and unlike a Task, an unhandled exception
            // on a raw dedicated Thread can crash the process. Leaking two small disposables in
            // this already-pathological (caller's callback never returned in time) case is the
            // safer trade-off.
            if (loopFinished)
            {
                _stopCts.Dispose();
                _signal.Dispose();
            }
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
                    DrainPendingTimerInserts();
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
                    DrainPendingTimerInserts();
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
            DrainPendingTimerInserts();
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

        // Always inline on the loop thread, never marshaled through _syncContext -- see the field
        // comment on _pendingTimerInserts for why this must not go through RunSafely/_mailbox.
        private void DrainPendingTimerInserts()
        {
            while (_pendingTimerInserts.TryDequeue(out var entry))
                InsertTimerSorted(entry);
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
                // Send (blocking marshal), not Post (fire-and-forget): by the time this call
                // returns, the work has actually run on the target context, matching every other
                // execution mode's RunSafely guarantee. This is what makes FinalDrain's "queued
                // work completes before Dispose returns" promise hold for SynchronizationContext
                // mode too -- with Post, Dispose could return (and PostAsync callers could hang
                // forever) while work was still only sitting on the dispatcher's queue, never
                // actually executed. Note: as with any blocking marshal onto a UI-style context,
                // callers must not invoke Dispose() synchronously *from* the actor's own target
                // context thread -- that would block the very thread this Send needs serviced,
                // the same well-known pitfall as any synchronous wait on captured-context work.
                _syncContext.Send(state =>
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
