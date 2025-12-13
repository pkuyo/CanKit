using System;
using System.Threading;

namespace CanKit.Core.Diagnostics;

public sealed class CanBusExceptionDispatcher
{
    private readonly string _component;
    private readonly CanExceptionPolicy _policy;
    private readonly Action<Exception> _raiseBackground;
    private readonly Action<Exception> _raiseFault;
    private readonly Action _stopBackground;
    private readonly Action<Exception> _failAsyncReceivers;

    private int _faulted;

    public CanBusExceptionDispatcher(
        string component,
        CanExceptionPolicy policy,
        Action<Exception> raiseBackground,
        Action<Exception> raiseFault,
        Action stopBackground,
        Action<Exception> failAsyncReceivers)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _raiseBackground = raiseBackground ?? throw new ArgumentNullException(nameof(raiseBackground));
        _raiseFault = raiseFault ?? throw new ArgumentNullException(nameof(raiseFault));
        _stopBackground = stopBackground ?? throw new ArgumentNullException(nameof(stopBackground));
        _failAsyncReceivers = failAsyncReceivers ?? throw new ArgumentNullException(nameof(failAsyncReceivers));
    }

    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    public void Report(
        Exception exception,
        CanExceptionSource source,
        CanExceptionSeverity? severity = null,
        string? message = null)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));

        if (_policy.IgnoreOperationCanceledException && exception is OperationCanceledException)
        {
            return;
        }

        var effectiveSeverity = severity ?? (_policy.Classifier?.Invoke(exception, source) ?? _policy.Classify(exception, source));
        var logMessage = message ?? $"{_component} exception ({source}, {effectiveSeverity}).";

        try
        {
            if (effectiveSeverity >= _policy.LogThreshold)
            {
                if (effectiveSeverity >= CanExceptionSeverity.Error)
                    CanKitLogger.LogError(logMessage, exception);
                else
                    CanKitLogger.LogWarning(logMessage, exception);
            }
        }
        catch
        {
            // best-effort logging only
        }

        var shouldNotify = effectiveSeverity >= _policy.BackgroundEventThreshold;
        var shouldFailAsync = effectiveSeverity >= _policy.AsyncReceiverFailThreshold;
        var shouldFault = effectiveSeverity >= _policy.FaultThreshold;

        if (shouldFailAsync)
        {
            try { _failAsyncReceivers(exception); } catch { /*ignore*/ }
        }

        if (shouldFault && Interlocked.Exchange(ref _faulted, 1) == 0)
        {
            try { _raiseFault(exception); } catch { /*ignore*/ }
            try { _stopBackground(); } catch { /*ignore*/ }
        }

        if (shouldNotify)
        {
            try { _raiseBackground(exception); } catch { /*ignore*/ }
        }
    }
}

