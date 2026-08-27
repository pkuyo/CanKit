using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.SocketCAN;

namespace CanKit.SocketCanArm32.Smoke;

internal static class Program
{
    private const string LibcTypeName = "CanKit.Adapter.SocketCAN.Native.Libc";
    private const string EpollEventTypeName = LibcTypeName + "+epoll_event";

    private static async Task<int> Main()
    {
        try
        {
            VerifyArm32Abi();
            await VerifyNativeTransmitAndReceive();
            Console.WriteLine("ARM32 SocketCAN smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyArm32Abi()
    {
        Require(
            RuntimeInformation.ProcessArchitecture == Architecture.Arm,
            $"Expected ARM process architecture, got {RuntimeInformation.ProcessArchitecture}.");

        var assembly = typeof(SocketCan).Assembly;
        var logicalType = assembly.GetType(EpollEventTypeName, throwOnError: true)!;
        Require(logicalType.GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .FieldType == typeof(ulong), "epoll_event.data must be an unsigned 64-bit value.");

        var packedType = assembly.GetType(LibcTypeName + "+epoll_event_packed", throwOnError: true)!;
        Require(Marshal.SizeOf(packedType) == 12, "Packed epoll_event must be 12 bytes.");
        Require(Marshal.OffsetOf(packedType, "data").ToInt32() == 4,
            "Packed epoll_event.data must start at offset 4.");

        var alignedType = assembly.GetType(LibcTypeName + "+epoll_event_aligned", throwOnError: true)!;
        Require(Marshal.SizeOf(alignedType) == 16, "ARM epoll_event must be 16 bytes.");
        Require(Marshal.OffsetOf(alignedType, "data").ToInt32() == 8,
            "ARM epoll_event.data must start at offset 8.");

        foreach (var (name, secondField) in new[] { ("timeval", "tv_usec"), ("timespec", "tv_nsec") })
        {
            var nativeLongType = typeof(SocketCan).Assembly.GetType(LibcTypeName + "+" + name, throwOnError: true)!;
            Require(Marshal.SizeOf(nativeLongType) == 8, $"{name} must be 8 bytes on ARM32.");
            Require(Marshal.OffsetOf(nativeLongType, secondField).ToInt32() == 4,
                $"{name}.{secondField} must start at offset 4 on ARM32.");
        }

        var bcmType = typeof(SocketCan).Assembly.GetType(LibcTypeName + "+bcm_msg_head", throwOnError: true)!;
        Require(Marshal.SizeOf(bcmType) == 36, "bcm_msg_head must be 36 bytes on ARM32.");
        Require(Marshal.OffsetOf(bcmType, "ival1").ToInt32() == 12,
            "bcm_msg_head.ival1 must start at offset 12 on ARM32.");
    }

    private static async Task VerifyNativeTransmitAndReceive()
    {
        using var rx = Open();
        using var tx = Open();

        var frames = Enumerable.Range(0, 20)
            .Select(static i => CanFrame.Classic(
                0x123 + i,
                new byte[] { (byte)i, 0x22, 0x33, 0x44 }))
            .ToArray();

        try
        {
            var receiveTask = rx.ReceiveAsync(frames.Length, 10_000);
            var sent = tx.Transmit(frames, 10_000);
            Require(sent == frames.Length, $"Expected to send {frames.Length} frames, sent {sent}.");

            var received = await receiveTask;
            try
            {
                Require(
                    received.Count == frames.Length,
                    $"Expected {frames.Length} frames, received {received.Count}.");

                var expectedIds = frames.Select(static frame => frame.ID);
                var actualIds = received.Select(static item => item.CanFrame.ID);
                Require(actualIds.SequenceEqual(expectedIds), "Received CAN IDs did not match transmitted IDs.");
            }
            finally
            {
                foreach (var item in received)
                    item.CanFrame.Dispose();
            }
        }
        finally
        {
            foreach (var frame in frames)
                frame.Dispose();
        }
    }

    private static SocketCanBus Open()
        => SocketCan.Open("vcan0", cfg =>
        {
            cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000);
            cfg.NetLink(false);
        });

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
