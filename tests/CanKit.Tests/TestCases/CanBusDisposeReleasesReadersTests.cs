using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Tests.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// A reader that is waiting in GetFramesAsync or ReceiveAsync(-1) must come back when the bus
/// is disposed, in every adapter.
/// </summary>
public class CanBusDisposeReleasesReadersTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    [Theory]
    [MemberData(nameof(Matrix.TestMatrix.Pairs), MemberType = typeof(Matrix.TestMatrix))]
    public async Task Pending_GetFramesAsync_Ends_When_The_Bus_Is_Disposed(string epA, string epB, string __, bool hasFd)
    {
        _ = (epB, __, hasFd);
        var bus = TestHelpers.OpenClassic(epA);
        var reading = ReadAllAsync(bus);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        reading.IsCompleted.Should().BeFalse("nothing was sent, so the enumeration must still be waiting");

        bus.Dispose();

        var completed = await Task.WhenAny(reading, Task.Delay(Grace, TestContext.Current.CancellationToken));
        completed.Should().BeSameAs(reading, "disposing the bus must end a pending GetFramesAsync");
        (await reading).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Matrix.TestMatrix.Pairs), MemberType = typeof(Matrix.TestMatrix))]
    public async Task Pending_Infinite_ReceiveAsync_Returns_When_The_Bus_Is_Disposed(string epA, string epB, string __, bool hasFd)
    {
        _ = (epB, __, hasFd);
        var bus = TestHelpers.OpenClassic(epA);
        var receiving = bus.ReceiveAsync(1, -1, TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        receiving.IsCompleted.Should().BeFalse("nothing was sent, so the receive must still be waiting");

        bus.Dispose();

        var completed = await Task.WhenAny(receiving, Task.Delay(Grace, TestContext.Current.CancellationToken));
        completed.Should().BeSameAs(receiving, "disposing the bus must end a pending ReceiveAsync(-1)");
        (await receiving).Should().BeEmpty();
    }

    private static async Task<List<CanReceiveData>> ReadAllAsync(ICanBus bus)
    {
        var list = new List<CanReceiveData>();
        await foreach (var item in bus.GetFramesAsync(TestContext.Current.CancellationToken))
            list.Add(item);
        return list;
    }
}
