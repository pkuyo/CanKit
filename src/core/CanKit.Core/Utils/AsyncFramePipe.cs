using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Core.Definitions;
using System.Threading.Channels;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Core.Utils;

public sealed class AsyncFramePipe
{
    private readonly Channel<CanReceiveData> _channel;

    private volatile TaskCompletionSource<Exception?> _exceptionPulse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public void Clear()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }

    public async Task<IReadOnlyList<CanReceiveData>> ReceiveBatchAsync(
        int count, int timeoutMs, CancellationToken cancellationToken)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var list = new List<CanReceiveData>((int)Math.Max(1, Math.Min(count, 256)));

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
                var readTask = _channel.Reader.ReadAsync(token).AsTask();
                var bgException = _exceptionPulse;

                var completed = await Task.WhenAny(readTask, bgException.Task).ConfigureAwait(false);
                if (completed == readTask)
                {
                    try
                    {
                        var item = await readTask.ConfigureAwait(false);
                        list.Add(item);
                    }
                    catch (OperationCanceledException)
                    {
                        return list;
                    }
                    catch (ChannelClosedException cce)
                    {
                        if (cce.InnerException is not null) throw cce.InnerException;
                        throw;
                    }
                }
                else
                {
                    var ex = await bgException.Task.ConfigureAwait(false);
                    throw ex ?? new InvalidOperationException("Exception signalled.");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return list;
        }
        finally
        {
            linkedCts?.Dispose();
        }

        return list;
    }

    public async IAsyncEnumerable<CanReceiveData> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var waitTask = _channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var faultSnap = _exceptionPulse;

            var completed = await Task.WhenAny(waitTask, faultSnap.Task).ConfigureAwait(false);
            if (completed == faultSnap.Task)
            {
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


    public void ExceptionOccured(Exception ex)
    {
        var old = Interlocked.Exchange(
            ref _exceptionPulse,
            new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously));
        _ = old.TrySetResult(ex);
    }
}
