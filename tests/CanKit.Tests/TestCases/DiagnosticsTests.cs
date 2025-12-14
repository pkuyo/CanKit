using System;
using System.Threading.Tasks;
using CanKit.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class DiagnosticsTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void CanExceptionPolicy_Defaults_Are_Expected()
    {
        var policy = new CanExceptionPolicy();

        policy.LogThreshold.Should().Be(CanExceptionSeverity.Error);
        policy.BackgroundEventThreshold.Should().Be(CanExceptionSeverity.Debug);
        policy.FaultThreshold.Should().Be(CanExceptionSeverity.Fault);
        policy.AsyncReceiverFailThreshold.Should().Be(CanExceptionSeverity.Fault);
        policy.SubscriberCallbackSeverity.Should().Be(CanExceptionSeverity.Error);
        policy.IgnoreOperationCanceledException.Should().BeTrue();
        policy.Classifier.Should().BeNull();
    }

    [Fact]
    public void Dispatcher_Ignore_OperationCanceledException_When_Configured()
    {
        var background = 0;
        var fault = 0;
        var stop = 0;
        var failAsync = 0;

        var dispatcher = new CanBusExceptionDispatcher(
            component: "test",
            policy: new CanExceptionPolicy
            {
                IgnoreOperationCanceledException = true,
                LogThreshold = CanExceptionSeverity.Fault,
                BackgroundEventThreshold = CanExceptionSeverity.Debug,
                FaultThreshold = CanExceptionSeverity.Fault,
                AsyncReceiverFailThreshold = CanExceptionSeverity.Fault
            },
            raiseBackground: _ => background++,
            raiseFault: _ => fault++,
            stopBackground: () => stop++,
            failAsyncReceivers: _ => failAsync++);

        dispatcher.Report(new OperationCanceledException("cancel"), CanExceptionSource.BackgroundLoop);

        dispatcher.IsFaulted.Should().BeFalse();
        background.Should().Be(0);
        fault.Should().Be(0);
        stop.Should().Be(0);
        failAsync.Should().Be(0);
    }

    [Fact]
    public void Dispatcher_Raises_Background_Event_For_OperationCanceled_When_Not_Ignored()
    {
        var background = 0;

        var dispatcher = new CanBusExceptionDispatcher(
            component: "test",
            policy: new CanExceptionPolicy
            {
                IgnoreOperationCanceledException = false,
                LogThreshold = CanExceptionSeverity.Fault,
                BackgroundEventThreshold = CanExceptionSeverity.Info,
                FaultThreshold = CanExceptionSeverity.Fault,
                AsyncReceiverFailThreshold = CanExceptionSeverity.Fault
            },
            raiseBackground: _ => background++,
            raiseFault: _ => { },
            stopBackground: () => { },
            failAsyncReceivers: _ => { });

        dispatcher.Report(new OperationCanceledException("cancel"), CanExceptionSource.BackgroundLoop);

        background.Should().Be(1);
        dispatcher.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public void Dispatcher_Uses_Classifier_And_Raises_Fault_Only_Once()
    {
        var background = 0;
        var fault = 0;
        var stop = 0;
        var classifierCalls = 0;
        CanExceptionSource? lastSource = null;

        var dispatcher = new CanBusExceptionDispatcher(
            component: "test",
            policy: new CanExceptionPolicy
            {
                IgnoreOperationCanceledException = false,
                LogThreshold = CanExceptionSeverity.Fault,
                BackgroundEventThreshold = CanExceptionSeverity.Debug,
                FaultThreshold = CanExceptionSeverity.Fault,
                AsyncReceiverFailThreshold = CanExceptionSeverity.Fault,
                Classifier = (_, src) =>
                {
                    classifierCalls++;
                    lastSource = src;
                    return CanExceptionSeverity.Fault;
                }
            },
            raiseBackground: _ => background++,
            raiseFault: _ => fault++,
            stopBackground: () => stop++,
            failAsyncReceivers: _ => { });

        dispatcher.Report(new InvalidOperationException("boom"), CanExceptionSource.BackgroundLoop);
        dispatcher.Report(new InvalidOperationException("boom2"), CanExceptionSource.BackgroundLoop);

        dispatcher.IsFaulted.Should().BeTrue();
        classifierCalls.Should().Be(2);
        lastSource.Should().Be(CanExceptionSource.BackgroundLoop);
        background.Should().Be(2);
        fault.Should().Be(1);
        stop.Should().Be(1);
    }
}

