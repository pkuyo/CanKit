using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ControlCAN;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Pins the managed layouts of the ControlCAN structs to the vendor header (ControlCAN.h).
/// The driver reads these structs byte for byte, so a wrong field width corrupts what it sees.
/// </summary>
public class ControlCanNativeLayoutTests
{
    private static readonly Type Api = typeof(ControlCanBus).Assembly.GetType(
        "CanKit.Adapter.ControlCAN.Native.ControlCAN", throwOnError: true)!;

    [Fact]
    public void VCI_FILTER_RECORD_Is_Three_DWORDs()
    {
        // typedef struct _VCI_FILTER_RECORD { DWORD ExtFrame; DWORD Start; DWORD End; }
        SizeOf("VCI_FILTER_RECORD").Should().Be(12);
        OffsetOf("VCI_FILTER_RECORD", "ExtFrame").Should().Be(0);
        OffsetOf("VCI_FILTER_RECORD", "Start").Should().Be(4);
        OffsetOf("VCI_FILTER_RECORD", "End").Should().Be(8);
    }

    [Fact]
    public void VCI_CAN_OBJ_Matches_Vendor_Header()
    {
        // UINT ID; UINT TimeStamp; BYTE TimeFlag, SendType, RemoteFlag, ExternFlag, DataLen; BYTE Data[8]; BYTE Reserved[3];
        SizeOf("VCI_CAN_OBJ").Should().Be(24);
        OffsetOf("VCI_CAN_OBJ", "ID").Should().Be(0);
        OffsetOf("VCI_CAN_OBJ", "TimeStamp").Should().Be(4);
        OffsetOf("VCI_CAN_OBJ", "TimeFlag").Should().Be(8);
        OffsetOf("VCI_CAN_OBJ", "DataLen").Should().Be(12);
        OffsetOf("VCI_CAN_OBJ", "Data").Should().Be(13);
        OffsetOf("VCI_CAN_OBJ", "Reserved").Should().Be(21);
    }

    [Fact]
    public void VCI_INIT_CONFIG_Matches_Vendor_Header()
    {
        // DWORD AccCode; DWORD AccMask; DWORD Reserved; UCHAR Filter, Timing0, Timing1, Mode;
        SizeOf("VCI_INIT_CONFIG").Should().Be(16);
        OffsetOf("VCI_INIT_CONFIG", "AccCode").Should().Be(0);
        OffsetOf("VCI_INIT_CONFIG", "AccMask").Should().Be(4);
        OffsetOf("VCI_INIT_CONFIG", "Reserved").Should().Be(8);
        OffsetOf("VCI_INIT_CONFIG", "Filter").Should().Be(12);
        OffsetOf("VCI_INIT_CONFIG", "Timing0").Should().Be(13);
        OffsetOf("VCI_INIT_CONFIG", "Timing1").Should().Be(14);
        OffsetOf("VCI_INIT_CONFIG", "Mode").Should().Be(15);
    }

    [Fact]
    public void VCI_AUTO_SEND_OBJ_Matches_Vendor_Header()
    {
        // BYTE Enable; BYTE Index; DWORD Interval; VCI_CAN_OBJ obj;
        SizeOf("VCI_AUTO_SEND_OBJ").Should().Be(32);
        OffsetOf("VCI_AUTO_SEND_OBJ", "Enable").Should().Be(0);
        OffsetOf("VCI_AUTO_SEND_OBJ", "Index").Should().Be(1);
        OffsetOf("VCI_AUTO_SEND_OBJ", "Interval").Should().Be(4);
        OffsetOf("VCI_AUTO_SEND_OBJ", "Obj").Should().Be(8);
    }

#if !FAKE
    [Fact]
    public void VCI_BOARD_INFO_And_VCI_ERR_INFO_Match_Vendor_Header()
    {
        // USHORT hw_Version, fw_Version, dr_Version, in_Version, irq_Num; BYTE can_Num;
        // CHAR str_Serial_Num[20]; CHAR str_hw_Type[40]; USHORT Reserved[4];
        SizeOf("VCI_BOARD_INFO").Should().Be(80);
        OffsetOf("VCI_BOARD_INFO", "can_Num").Should().Be(10);
        OffsetOf("VCI_BOARD_INFO", "str_Serial").Should().Be(11);
        OffsetOf("VCI_BOARD_INFO", "str_hw_Type").Should().Be(31);
        OffsetOf("VCI_BOARD_INFO", "Reserved").Should().Be(72);

        // UINT ErrCode; BYTE Passive_ErrData[3]; BYTE ArLost_ErrData;
        SizeOf("VCI_ERR_INFO").Should().Be(8);
        OffsetOf("VCI_ERR_INFO", "Passive_ErrData").Should().Be(4);
        OffsetOf("VCI_ERR_INFO", "ArLost_ErrData").Should().Be(7);
    }
#endif

    private static int SizeOf(string nested) => Marshal.SizeOf(Nested(nested));

    private static int OffsetOf(string nested, string field) => (int)Marshal.OffsetOf(Nested(nested), field);

    private static Type Nested(string name) =>
        Api.GetNestedType(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Struct {name} not found in ControlCAN binding.");
}
