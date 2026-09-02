#if !FAKE
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using CanKit.Adapter.SocketCAN;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class SocketCanNativeAbiTests
{
    [Fact]
    public void Managed_Layouts_And_Signatures_Match_Linux_SocketCan_Abi()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var libc = typeof(SocketCanProvider).Assembly.GetType(
            "CanKit.Adapter.SocketCAN.Native.Libc", throwOnError: true)!;
        var pointerSize = IntPtr.Size;

        SizeOf(libc, "sockaddr_can").Should().Be(24);
        SizeOf(libc, "can_frame").Should().Be(16);
        SizeOf(libc, "canfd_frame").Should().Be(72);
        SizeOf(libc, "iovec").Should().Be(pointerSize == 4 ? 8 : 16);
        SizeOf(libc, "msghdr").Should().Be(pointerSize == 4 ? 28 : 56);
        SizeOf(libc, "mmsghdr").Should().Be(pointerSize == 4 ? 32 : 64);
        SizeOf(libc, "timespec").Should().Be(pointerSize == 4 ? 8 : 16);
        SizeOf(libc, "cmsghdr").Should().Be(pointerSize == 4 ? 12 : 16);
        SizeOf(libc, "can_filter").Should().Be(8);
        SizeOf(libc, "pollfd").Should().Be(8);
        SizeOf(libc, "timeval").Should().Be(pointerSize == 4 ? 8 : 16);

        OffsetOf(libc, "sockaddr_can", "can_family").Should().Be(0);
        OffsetOf(libc, "sockaddr_can", "can_ifindex").Should().Be(4);
        OffsetOf(libc, "sockaddr_can", "rx_id").Should().Be(8);
        OffsetOf(libc, "sockaddr_can", "tx_id").Should().Be(12);
        OffsetOf(libc, "can_frame", "data").Should().Be(8);
        OffsetOf(libc, "canfd_frame", "data").Should().Be(8);

        var managedBcmHeaderSize = SizeOf(libc, "bcm_msg_head");
        var wireBcmHeaderSize = (int)libc.GetProperty(
            "BcmWireHeaderSize", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        wireBcmHeaderSize.Should().Be((managedBcmHeaderSize + 7) & ~7);
        wireBcmHeaderSize.Should().Be(pointerSize == 4 ? 40 : 56);
        OffsetOf(libc, "bcm_msg_head", "nframes").Should().Be(pointerSize == 4 ? 32 : 52);

        SizeOf(libc, "epoll_event_packed").Should().Be(12);
        SizeOf(libc, "epoll_event_aligned").Should().Be(16);

        var libSocketCan = typeof(SocketCanProvider).Assembly.GetType(
            "CanKit.Adapter.SocketCAN.Native.LibSocketCan", throwOnError: true)!;
        SizeOf(libSocketCan, "can_bittiming").Should().Be(32);
        SizeOf(libSocketCan, "can_bittiming_const").Should().Be(48);
        SizeOf(libSocketCan, "can_clock").Should().Be(4);
        SizeOf(libSocketCan, "can_berr_counter").Should().Be(4);
        SizeOf(libSocketCan, "can_ctrlmode").Should().Be(8);
        SizeOf(libSocketCan, "can_device_stats").Should().Be(24);
        SizeOf(libSocketCan, "rtnl_link_stats64").Should().Be(200);

        Constant(libc, "SO_SNDBUF").Should().Be(7);
        Constant(libc, "SO_RCVBUF").Should().Be(8);

        Method(libc, "read").ReturnType.Should().Be(typeof(nint));
        Method(libc, "read").GetParameters()[2].ParameterType.Should().Be(typeof(nuint));
        Method(libc, "write").ReturnType.Should().Be(typeof(nint));
        Method(libc, "write").GetParameters()[2].ParameterType.Should().Be(typeof(nuint));
        Method(libc, "recvmsg").ReturnType.Should().Be(typeof(nint));
        Method(libc, "poll").GetParameters()[1].ParameterType.Should().Be(typeof(nuint));
        Method(libc, "bind").GetParameters()[2].ParameterType.Should().Be(typeof(uint));
        Method(libc, "connect").GetParameters()[2].ParameterType.Should().Be(typeof(uint));
    }

    private static Type NativeType(Type libc, string name)
        => libc.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
           ?? throw new MissingMemberException(libc.FullName, name);

    private static int SizeOf(Type libc, string name)
        => Marshal.SizeOf(NativeType(libc, name));

    private static int OffsetOf(Type libc, string typeName, string fieldName)
        => Marshal.OffsetOf(NativeType(libc, typeName), fieldName).ToInt32();

    private static int Constant(Type libc, string name)
        => (int)libc.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

    private static MethodInfo Method(Type libc, string name)
        => libc.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
           ?? throw new MissingMethodException(libc.FullName, name);
}
#endif
