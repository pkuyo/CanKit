using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils;

public sealed class AsyncFramePipe<T>
{
    private readonly Channel<T> _channel;

    private volatile TaskCompletionSource<Exception?> _exceptionPulse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AsyncFramePipe(int? capacity = null, Action<T>? onDropped = null)
    {
        if (capacity.HasValue)
        {
            var opt = new BoundedChannelOptions(capacity.Value)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            };

            _channel = onDropped is null
                ? Channel.CreateBounded<T>(opt)
                : Channel.CreateBounded<T>(opt, onDropped);
        }
        else
        {
            _channel = Channel.CreateUnbounded<T>();
        }
    }

    public void Publish(T frame)
    {
        _ = _channel.Writer.TryWrite(frame);
    }

    public void Clear()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> frames, or until timeout/channel completion when
    /// <paramref name="count"/> is 0. Timeout cancellation returns the frames already read, while
    /// caller-provided cancellation propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<IReadOnlyList<T>> ReceiveBatchAsync(
        int count, int timeoutMs, CancellationToken cancellationToken)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        cancellationToken.ThrowIfCancellationRequested();

        var list = new List<T>((int)Math.Max(1, Math.Min(count, 256)));

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
                cancellationToken.ThrowIfCancellationRequested();

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var waitTask = _channel.Reader.WaitToReadAsync(waitCts.Token).AsTask();
                var bgException = _exceptionPulse;

                var completed = await Task.WhenAny(waitTask, bgException.Task).ConfigureAwait(false);
                if (completed != waitTask)
                {
                    waitCts.Cancel();
                    try
                    {
                        _ = await waitTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Losing wait canceled for cleanup; prefer caller cancellation below.
                    }

                    // Caller cancel must win over a raced background pulse (method contract).
                    cancellationToken.ThrowIfCancellationRequested();

                    // Propagate pulsed faults as-is (including OCE/TCE). There is intentionally no
                    // outer timeout catch that could disguise these as a partial-batch timeout.
                    var fault = await bgException.Task.ConfigureAwait(false);
                    throw fault ?? new InvalidOperationException("Exception signalled.");
                }

                try
                {
                    if (!await waitTask.ConfigureAwait(false))
                    {
                        break;
                    }

                    if (_channel.Reader.TryRead(out var item))
                    {
                        list.Add(item);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout (or timeout racing ExceptionOccured): prefer an already-signalled
                    // fault, including pulsed OCE/TCE, over returning a partial timeout result.
                    if (bgException.Task.IsCompleted)
                    {
                        var fault = await bgException.Task.ConfigureAwait(false);
                        throw fault ?? new InvalidOperationException("Exception signalled.");
                    }

                    return list;
                }
                catch (ChannelClosedException cce)
                {
                    if (cce.InnerException is not null) throw cce.InnerException;
                    throw;
                }
            }
        }
        finally
        {
            linkedCts?.Dispose();
        }

        return list;
    }

    public async IAsyncEnumerable<T> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var waitTask = _channel.Reader.WaitToReadAsync(waitCts.Token).AsTask();
            var faultSnap = _exceptionPulse;

            var completed = await Task.WhenAny(waitTask, faultSnap.Task).ConfigureAwait(false);
            if (completed == faultSnap.Task)
            {
                waitCts.Cancel();
                try
                {
                    _ = await waitTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Losing wait canceled for cleanup; the fault below is the outcome.
                }

                var ex = await faultSnap.Task.ConfigureAwait(false);
                throw ex ?? new InvalidOperationException("Fault signalled.");
            }

            if (!await waitTask.ConfigureAwait(false))
                break;

            while (_channel.Reader.TryRead(out var item))
                yield return item;
        }

        try
        {
            await _channel.Reader.Completion.ConfigureAwait(false);
        }
        catch (ChannelClosedException cce)
        {
            if (cce.InnerException is not null) throw cce.InnerException;
            throw;
        }
    }


    /// <summary>
    /// Wakes readers currently waiting on this pipe with <paramref name="ex"/>. The pulse is not
    /// sticky: readers that start after this call wait for the next frame or next exception pulse.
    /// </summary>
    public void ExceptionOccured(Exception ex)
    {
        var old = Interlocked.Exchange(
            ref _exceptionPulse,
            new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously));
        _ = old.TrySetResult(ex);
    }
}
