using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// NFR-012 (Should): received-frame <see cref="CanReceiveData.SystemTimestamp"/> must carry
/// <see cref="DateTimeKind.Utc"/> so downstream logging/tracing has a single wall-clock UTC
/// time base regardless of the host's local zone or daylight-saving transitions.
/// UTC is not monotonic (leap seconds / manual clock adjustments still apply); the goal here
/// is a zone-independent, comparable stamp, not a monotonic clock.
/// </summary>
public class SystemTimestampUtcTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"utc-ts-{Guid.NewGuid():N}";

    [Fact]
    public void Default_SystemTimestamp_Is_Utc()
    {
        // The CanReceiveData record-struct default initializer must produce a UTC timestamp
        // even when a producer forgets to set it explicitly (e.g. the Virtual hub path).
        var frame = CanFrame.Classic(0x100, ReadOnlyMemory<byte>.Empty);
        var data = new CanReceiveData(frame);

        data.SystemTimestamp.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Virtual_Received_Frame_Has_Utc_SystemTimestamp()
    {
        var session = NewSession();

        using var sender = CanBus.Open($"virtual://{session}/0", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20)
                .Baud(TestCaseProvider.AbitRate)
                .SetAsyncBufferCapacity(16));
        using var receiver = CanBus.Open($"virtual://{session}/1", cfg =>
            cfg.SetProtocolMode(CanProtocolMode.Can20)
                .Baud(TestCaseProvider.AbitRate)
                .SetAsyncBufferCapacity(16));

        var before = DateTime.UtcNow;
        sender.Transmit(CanFrame.Classic(0x123, new byte[] { 1, 2, 3 }));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = (await receiver.ReceiveAsync(1, 2000, cts.Token)).Single();
        var after = DateTime.UtcNow;

        try
        {
            received.SystemTimestamp.Kind.Should().Be(DateTimeKind.Utc);
            // Sanity check: the stamp lies in the UTC window that brackets the transmit call.
            // A stray DateTime.Now would fall outside this range in any non-UTC time zone.
            received.SystemTimestamp.Should().BeOnOrAfter(before.AddSeconds(-1));
            received.SystemTimestamp.Should().BeOnOrBefore(after.AddSeconds(1));
        }
        finally
        {
            received.CanFrame.Dispose();
        }
    }
}
