using System;
using System.Linq;
using System.Reflection;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.SocketCAN;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

[Trait("Category", "FakeOnly")]
public class SocketCanTransmitRegressionTests
{
    private const string LibcTypeName = "CanKit.Adapter.SocketCAN.Native.Libc";

#if FAKE
    [Theory]
    [InlineData(CanProtocolMode.Can20, false)]
    [InlineData(CanProtocolMode.Can20, true)]
    [InlineData(CanProtocolMode.CanFd, false)]
    [InlineData(CanProtocolMode.CanFd, true)]
    public void Batch_Transmit_Retries_After_ENOBUFS(CanProtocolMode mode, bool asEnumerable)
    {
        if (!IsSocketCanTarget())
            return;

        using var bus = Open(mode);
        var frames = CreateFrames(mode, 20);

        FailNextSendWithEnobufs();

        var sent = asEnumerable
            ? bus.Transmit(frames.Select(static frame => frame), 3000)
            : bus.Transmit(frames, 3000);

        sent.Should().Be(frames.Length);
    }

    [Theory]
    [InlineData(CanProtocolMode.Can20)]
    [InlineData(CanProtocolMode.CanFd)]
    public void Single_Transmit_Returns_Zero_After_ENOBUFS(CanProtocolMode mode)
    {
        if (!IsSocketCanTarget())
            return;

        using var bus = Open(mode);
        var frame = CreateFrames(mode, 1)[0];

        FailNextSendWithEnobufs();

        bus.Transmit(frame).Should().Be(0);
    }

    private static bool IsSocketCanTarget()
        => string.Equals(
            Environment.GetEnvironmentVariable("CANKIT_TEST_ADAPTERS"),
            "CanKit.Adapter.SocketCAN", StringComparison.OrdinalIgnoreCase);

    private static SocketCanBus Open(CanProtocolMode mode)
        => SocketCan.Open("vcan0", cfg =>
        {
            if (mode == CanProtocolMode.CanFd)
                cfg.SetProtocolMode(mode).Fd(500_000, 2_000_000);
            else
                cfg.SetProtocolMode(mode).Baud(500_000);

            cfg.NetLink(false);
        });

    private static void FailNextSendWithEnobufs()
    {
        var type = typeof(SocketCan).Assembly.GetType(LibcTypeName, throwOnError: true)!;
        var method = type.GetMethod("FailNextSendWith", BindingFlags.Static | BindingFlags.NonPublic)!;
        var errno = (int)type.GetField("ENOBUFS")!.GetRawConstantValue()!;
        method.Invoke(null, [errno]);
    }
#endif

    private static CanFrame[] CreateFrames(CanProtocolMode mode, int count)
    {
        var frames = new CanFrame[count];
        for (var i = 0; i < frames.Length; i++)
        {
            var payload = new byte[] { (byte)i, 0x22, 0x33, 0x44 };
            frames[i] = mode == CanProtocolMode.CanFd
                ? CanFrame.Fd(0x123 + i, payload)
                : CanFrame.Classic(0x123 + i, payload);
        }

        return frames;
    }
}
