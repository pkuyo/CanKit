using System.Linq;
using System.Runtime.InteropServices;
using CanKit.Adapter.ZLG.Native;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Pins the layout of ZCAN_CHANNEL_INIT_CONFIG against zlgcan.h:
/// <c>struct { UINT can_type; union { can (16 bytes); canfd (28 bytes); }; }</c>.
/// Only type metadata is inspected, so the native library is not needed.
/// </summary>
public class ZlgNativeLayoutTests
{
    [Fact]
    public void Init_Config_Is_A_Tagged_Union_Of_32_Bytes()
    {
        Marshal.SizeOf<ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG>().Should().Be(16);
        Marshal.SizeOf<ZLGCAN._ZCAN_CHANNEL_CANFD_INIT_CONFIG>().Should().Be(28);
        Marshal.SizeOf<ZLGCAN._ZCAN_CHANNEL_INIT_CONFIG>().Should().Be(28);
        Marshal.SizeOf<ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG>().Should().Be(32);

        Offset<ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG>(nameof(ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG.config)).Should().Be(4);
        Offset<ZLGCAN._ZCAN_CHANNEL_INIT_CONFIG>(nameof(ZLGCAN._ZCAN_CHANNEL_INIT_CONFIG.can)).Should().Be(0);
        Offset<ZLGCAN._ZCAN_CHANNEL_INIT_CONFIG>(nameof(ZLGCAN._ZCAN_CHANNEL_INIT_CONFIG.canfd)).Should().Be(0);
    }

    [Fact]
    public void Filter_And_Mode_Sit_At_Different_Offsets_In_The_Two_Views()
    {
        Offset<ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG>(nameof(ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG.filter)).Should().Be(12);
        Offset<ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG>(nameof(ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG.mode)).Should().Be(15);

        Offset<ZLGCAN._ZCAN_CHANNEL_CANFD_INIT_CONFIG>(nameof(ZLGCAN._ZCAN_CHANNEL_CANFD_INIT_CONFIG.filter)).Should().Be(20);
        Offset<ZLGCAN._ZCAN_CHANNEL_CANFD_INIT_CONFIG>(nameof(ZLGCAN._ZCAN_CHANNEL_CANFD_INIT_CONFIG.mode)).Should().Be(21);
    }

    [Fact]
    public void Marshalled_Bytes_Put_Canfd_Filter_And_Mode_Where_The_Driver_Reads_Them()
    {
        var config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG { can_type = 1 };
        config.config.canfd.acc_mask = 0xffffffff;
        config.config.canfd.filter = 1;
        config.config.canfd.mode = 1;

        var raw = ToBytes(config);

        raw.Should().HaveCount(32);
        raw[4 + 20].Should().Be(1, "canfd.filter is at union offset 20");
        raw[4 + 21].Should().Be(1, "canfd.mode is at union offset 21");
        // dbit_timing (union offset 12..15) must stay untouched.
        raw.Skip(4 + 12).Take(4).Should().OnlyContain(b => b == 0);
    }

    private static byte[] ToBytes<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, ptr, false);
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);
}
