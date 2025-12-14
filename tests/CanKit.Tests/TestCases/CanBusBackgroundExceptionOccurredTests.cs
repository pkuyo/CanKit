using System;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core;
using CanKit.Core.Utils;
using CanKit.Tests.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class CanBusBackgroundExceptionOccurredTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [MemberData(nameof(Matrix.TestMatrix.Pairs), MemberType = typeof(Matrix.TestMatrix))]
    public async Task BackgroundExceptionOccurred_Is_Raised_When_ReceiveThread_Hits_Exception(string epA, string epB, string _, bool hasFd)
    {
        using var rxClassic = TestHelpers.OpenClassic(epA);
        using var txClassic = TestHelpers.OpenClassic(epB);
        using AutoResetEvent ev = new AutoResetEvent(false);
        using CancellationTokenSource cts = new CancellationTokenSource();
        Exception? exception = null;
        rxClassic.FrameReceived += (sender, data) =>
        {
            throw new ArgumentException("Should throw");
        };
        rxClassic.BackgroundExceptionOccurred += (sender, data) =>
        {
            exception = data;
            ev.Set();
        };
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        txClassic.Transmit(CanFrame.Classic(0x123456, ReadOnlyMemory<byte>.Empty, true));

        WaitHandle.WaitAny([ ev, cts.Token.WaitHandle ]);
        exception.Should().BeOfType<ArgumentException>();

    }
}
