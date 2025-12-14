using System;
using CanKit.Core.Exceptions;

namespace CanKit.Core.Diagnostics;

public enum CanExceptionSeverity
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fault = 4,
}

public enum CanExceptionSource
{
    BackgroundLoop = 1,
    SubscriberCallback = 2,
    Unknown = 255,
}

public sealed class CanExceptionPolicy
{
    public static CanExceptionPolicy Default { get; set; } = new();

    public CanExceptionSeverity LogThreshold { get; init; } = CanExceptionSeverity.Error;

    public CanExceptionSeverity BackgroundEventThreshold { get; init; } = CanExceptionSeverity.Debug;

    public CanExceptionSeverity FaultThreshold { get; init; } = CanExceptionSeverity.Fault;

    public CanExceptionSeverity AsyncReceiverFailThreshold { get; init; } = CanExceptionSeverity.Fault;

    public CanExceptionSeverity SubscriberCallbackSeverity { get; init; } = CanExceptionSeverity.Error;

    public bool IgnoreOperationCanceledException { get; init; } = true;

    public Func<Exception, CanExceptionSource, CanExceptionSeverity>? Classifier { get; init; }

    internal CanExceptionSeverity Classify(Exception exception, CanExceptionSource source)
    {
        if (exception is OperationCanceledException)
            return CanExceptionSeverity.Info;

        if (exception is CanBusDisposedException && source == CanExceptionSource.BackgroundLoop)
            return CanExceptionSeverity.Info;

        if (exception is CanKitException)
            return CanExceptionSeverity.Error;

        return CanExceptionSeverity.Error;
    }
}

