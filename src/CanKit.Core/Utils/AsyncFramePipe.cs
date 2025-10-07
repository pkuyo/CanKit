using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using System.Threading.Tasks;
using CanKit.Core.Definitions;
#if NET8_0_OR_GREATER
using System.Threading.Channels;
#endif
namespace CanKit.Core.Utils
{
    /// <summary>
    /// Internal helper to bridge push-based frame sources to async consumers.
    /// - On .NET 8+, uses Channel + IAsyncEnumerable
    /// - On netstandard2.0, uses ConcurrentQueue + TaskCompletionSource
    /// </summary>
    public sealed class AsyncFramePipe
    {
#if NET8_0_OR_GREATER
        private readonly Channel<CanReceiveData> _channel;

        public AsyncFramePipe(int? capacity = null)
        {
            if (capacity.HasValue)
            {
                var opt = new BoundedChannelOptions(capacity.Value)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest
                };
                _channel = Channel.CreateBounded<CanReceiveData>(opt);
            }
            else
            {
                var opt = new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false
                };
                _channel = Channel.CreateUnbounded<CanReceiveData>(opt);
            }
        }

        public void Publish(CanReceiveData frame)
        {
            _ = _channel.Writer.TryWrite(frame);
        }

        public async Task<IReadOnlyList<CanReceiveData>> ReceiveBatchAsync(
            uint count, int timeoutMs, CancellationToken cancellationToken)
        {
            var list = new List<CanReceiveData>((int)Math.Max(1, Math.Min(count, 256)));

            // Non-blocking fast path
            if (timeoutMs == 0)
            {
                while ((count == 0 || list.Count < count) && _channel.Reader.TryRead(out var item))
                    list.Add(item);
                return list;
            }

            CancellationToken token = cancellationToken;
            CancellationTokenSource? linkedCts = null;
            try
            {
                if (timeoutMs > 0)
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    linkedCts.CancelAfter(timeoutMs);
                    token = linkedCts.Token;
                }

                while (count == 0 || list.Count < count)
                {
                    var item = await _channel.Reader.ReadAsync(token).ConfigureAwait(false);
                    list.Add(item);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                linkedCts?.Dispose();
            }

            return list;
        }

        public async IAsyncEnumerable<CanReceiveData> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }

#else
        private readonly ConcurrentQueue<CanReceiveData> _queue = new ConcurrentQueue<CanReceiveData>();
        private readonly int? _capacity;
        private readonly object _gate = new object();
        private List<TaskCompletionSource<bool>>? _waiters;

        public AsyncFramePipe(int? capacity = null)
        {
            _capacity = capacity;
        }

        public void Publish(CanReceiveData frame)
        {
            if (_capacity.HasValue)
            {
                // Drop oldest to maintain bounded queue
                while (_queue.Count >= _capacity.Value && _queue.TryDequeue(out _)) { }
            }
            _queue.Enqueue(frame);
            List<TaskCompletionSource<bool>>? toWake = null;
            lock (_gate)
            {
                if (_waiters is { Count: > 0 })
                {
                    toWake = _waiters;
                    _waiters = null;
                }
            }
            if (toWake != null)
            {
                foreach (var t in toWake)
                {
                    _ = t.TrySetResult(true);
                }
            }
        }

        public async Task<IReadOnlyList<CanReceiveData>> ReceiveBatchAsync(
            uint count, int timeoutMs, CancellationToken cancellationToken)
        {
            var list = new List<CanReceiveData>((int)Math.Max(1, Math.Min(count, 256)));

            if (timeoutMs == 0)
            {
                while ((count == 0 || list.Count < count) && _queue.TryDequeue(out var item))
                    list.Add(item);
                return list;
            }

            Stopwatch? sw = timeoutMs > 0 ? Stopwatch.StartNew() : null;

            while (count == 0 || list.Count < count)
            {
                while ((count == 0 || list.Count < count) && _queue.TryDequeue(out var item))
                    list.Add(item);
                if (count != 0 && list.Count >= count) break;

                //重新创建TaskCompletionSource，等待下一次接收数据
                TaskCompletionSource<bool> tcs;
                lock (_gate)
                {
                    tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters ??= new List<TaskCompletionSource<bool>>();
                    _waiters.Add(tcs);
                }

                try
                {
                    if (sw != null) // 有超时
                    {
                        var remaining = Math.Max(0, timeoutMs - (int)sw.ElapsedMilliseconds);
                        var delay = Task.Delay(remaining, cancellationToken);
                        var completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
                        if (completed == delay)
                        {
                            break;
                        }

                        await tcs.Task.ConfigureAwait(false);
                    }
                    else
                    {
                        var cancelTcs =
                            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        using (cancellationToken.Register(() => cancelTcs.TrySetResult(true)))
                        {
                            var completed = await Task.WhenAny(tcs.Task, cancelTcs.Task).ConfigureAwait(false);
                            if (completed == cancelTcs.Task)
                            {
                                break;
                            }
                            await tcs.Task.ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    lock (_gate)
                    {
                        _waiters?.Remove(tcs);
                    }
                }
            }

            return list;
        }

#endif
    }
}
