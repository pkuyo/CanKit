using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.SocketCAN;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class SocketCanArmRegressionTests
{
    private const string LibcTypeName = "CanKit.Adapter.SocketCAN.Native.Libc";
    private const string EpollEventTypeName = LibcTypeName + "+epoll_event";

    [Fact]
    public void EpollEvent_Matches_Linux_Packed_Abi()
    {
        var type = typeof(SocketCan).Assembly.GetType(EpollEventTypeName, throwOnError: true)!;

        Marshal.SizeOf(type).Should().Be(12);
        Marshal.OffsetOf(type, "data").ToInt32().Should().Be(4);
        type.GetField("data")!.FieldType.Should().Be(typeof(ulong));
    }

    [Fact]
    public void NativeTimeStructs_Match_Linux_Long_Abi()
    {
        var assembly = typeof(SocketCan).Assembly;
        foreach (var (name, secondField) in new[] { ("timeval", "tv_usec"), ("timespec", "tv_nsec") })
        {
            var type = assembly.GetType(LibcTypeName + "+" + name, throwOnError: true)!;
            Marshal.SizeOf(type).Should().Be(IntPtr.Size * 2);
            Marshal.OffsetOf(type, secondField).ToInt32().Should().Be(IntPtr.Size);
            type.GetField("tv_sec")!.FieldType.Should().Be(typeof(IntPtr));
            type.GetField(secondField)!.FieldType.Should().Be(typeof(IntPtr));
        }

        var bcmType = assembly.GetType(LibcTypeName + "+bcm_msg_head", throwOnError: true)!;
        var firstTimevalOffset = IntPtr.Size == 8 ? 16 : 12;
        var timevalSize = IntPtr.Size * 2;
        Marshal.OffsetOf(bcmType, "ival1").ToInt32().Should().Be(firstTimevalOffset);
        Marshal.OffsetOf(bcmType, "ival2").ToInt32().Should().Be(firstTimevalOffset + timevalSize);
        Marshal.SizeOf(bcmType).Should().Be(firstTimevalOffset + (timevalSize * 2) + 8);
    }

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

#if !FAKE
    [Fact]
    public async Task Arm32_Native_SocketCan_Receives_And_Batch_Transmits()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CANKIT_SOCKETCAN_ARM32_CI"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RuntimeInformation.ProcessArchitecture.Should().Be(Architecture.Arm);

        using var rx = SocketCan.Open("vcan0", cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
            cfg.NetLink(false);
        });
        using var tx = SocketCan.Open("vcan0", cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
            cfg.NetLink(false);
        });

        var frames = CreateFrames(CanProtocolMode.Can20, 20);
        var receiveTask = rx.ReceiveAsync(frames.Length, 3000);

        tx.Transmit(frames, 3000).Should().Be(frames.Length);

        var received = await receiveTask;
        try
        {
            received.Should().HaveCount(frames.Length);
            received.Select(static item => item.CanFrame.ID)
                .Should().Equal(frames.Select(static frame => frame.ID));
        }
        finally
        {
            foreach (var item in received)
                item.CanFrame.Dispose();
        }
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
