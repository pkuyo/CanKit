#if FAKE
using System;
using System.Reflection;
using System.Threading;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.PCAN;
using CanKit.Adapter.SocketCAN;
using CanKit.Adapter.Vector;
using CanKit.Adapter.Vector.Native;
using CanKit.Adapter.ZLG;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Native;
using FluentAssertions;
using Peak.Can.Basic;
using Xunit;

namespace CanKit.Tests.TestCases;

public class ControllerBusStateTests
{
    private const PcanStatus UnrecognizedPcanStatus = (PcanStatus)0x40000000;
    private const int UnrecognizedKvaserStatus = 0x40000000;

    [Theory]
    [InlineData(0, 0, BusState.ErrActive)]
    [InlineData(0, 1, BusState.ErrWarning)]
    [InlineData(0, 2, BusState.ErrPassive)]
    [InlineData(0, 3, BusState.BusOff)]
    [InlineData(0, 4, BusState.Unknown)] // stopped
    [InlineData(0, 5, BusState.Unknown)] // sleeping
    [InlineData(0, 6, BusState.Unknown)] // sentinel, not a state
    [InlineData(0, 123, BusState.Unknown)]
    [InlineData(-1, 0, BusState.Unknown)] // failed query with active output
    [InlineData(-1, 3, BusState.Unknown)]
    public void SocketCan_Requires_A_Successful_Known_Controller_State(int result, int native, BusState expected)
    {
        using var bus = SocketCan.Open("vcan0", cfg => cfg.Baud(500_000).NetLink(false));
        try
        {
            SetSocketCanStatus(result, native);
            bus.BusState.Should().Be(expected);
        }
        finally
        {
            SetSocketCanStatus(0, 0);
        }
    }

    [Fact]
    public void SocketCan_Query_Error_Does_Not_Pretend_To_Be_Recovery()
    {
        using var bus = SocketCan.Open("vcan0", cfg => cfg.Baud(500_000).NetLink(false));
        try
        {
            SetSocketCanStatus(0, 3);
            bus.BusState.Should().Be(BusState.BusOff);
            SetSocketCanStatus(-1, 0);
            bus.BusState.Should().Be(BusState.Unknown);
            SetSocketCanStatus(0, 0);
            bus.BusState.Should().Be(BusState.ErrActive);
        }
        finally
        {
            SetSocketCanStatus(0, 0);
        }
    }

    private static void SetSocketCanStatus(int result, int state)
        => typeof(SocketCan).Assembly.GetType("CanKit.Adapter.SocketCAN.Native.LibSocketCan", true)!
            .GetMethod("SetStatus", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { "vcan0", result, state });

    [Theory]
    [InlineData(CanProtocolMode.Can20)]
    [InlineData(CanProtocolMode.CanFd)]
    public void Vector_Requires_A_Recognized_Chip_State_Event(CanProtocolMode mode)
    {
        using var bus = Vector.Open(0, cfg =>
        {
            cfg.SetProtocolMode(mode);
            if (mode == CanProtocolMode.CanFd) cfg.Fd(500_000, 2_000_000);
            else cfg.Baud(500_000);
        });
        bus.BusState.Should().Be(BusState.Unknown);
        bus.RequestBusState(); // A successful request alone is not a state event.
        bus.BusState.Should().Be(BusState.Unknown);
        foreach (var entry in new (byte Native, BusState Expected)[]
        {
            ((byte)VxlApi.XL_CHIPSTAT_BUSOFF, BusState.BusOff),
            (0x80, BusState.Unknown),
            ((byte)VxlApi.XL_CHIPSTAT_ERROR_ACTIVE, BusState.ErrActive),
            ((byte)VxlApi.XL_CHIPSTAT_ERROR_PASSIVE, BusState.ErrPassive),
            (0, BusState.Unknown),
            ((byte)VxlApi.XL_CHIPSTAT_ERROR_WARNING, BusState.ErrWarning),
            ((byte)VxlApi.XL_CHIPSTAT_ERROR_ACTIVE, BusState.ErrActive)
        })
        {
            VxlApi.InjectChipState(bus.Handle, entry.Native);
            SpinWait.SpinUntil(() => bus.BusState == entry.Expected, TimeSpan.FromSeconds(3))
                .Should().BeTrue($"chip state {entry.Native} should map to {entry.Expected}");
        }
    }

    [Theory]
    [InlineData(1u, ZlgErrorFlag.BusOff, 255, 255, BusState.BusOff)]
    [InlineData(1u, ZlgErrorFlag.BusErr, 128, 96, BusState.ErrPassive)]
    [InlineData(1u, ZlgErrorFlag.BusErr, 96, 128, BusState.ErrPassive)]
    [InlineData(1u, ZlgErrorFlag.BusErr, 96, 0, BusState.ErrWarning)]
    [InlineData(1u, ZlgErrorFlag.BusErr, 0, 96, BusState.ErrWarning)]
    [InlineData(1u, ZlgErrorFlag.None, 0, 0, BusState.Unknown)]
    [InlineData(1u, ZlgErrorFlag.FifoOverflow, 0, 0, BusState.Unknown)]
    [InlineData(1u, ZlgErrorFlag.BusErr, 1, 1, BusState.Unknown)]
    [InlineData(1u, ZlgErrorFlag.DeviceNotExist, 0, 0, BusState.Unknown)]
    [InlineData(1u, (ZlgErrorFlag)0x40000000, 0, 0, BusState.Unknown)]
    [InlineData(0u, ZlgErrorFlag.BusOff, 255, 255, BusState.Unknown)]
    public void Zlg_Diagnostics_Without_Controller_Evidence_Are_Unknown(uint result, ZlgErrorFlag flags, byte rec, byte tec, BusState expected)
    {
        using var bus = ZlgCan.Open(ZlgDeviceType.ZCAN_USBCAN2, 0, 0, cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
        ZLGCAN.SetErrorInfo(bus.Handle, result, (uint)flags, rec, tec);
        bus.BusState.Should().Be(expected);
    }

    [Fact]
    public void Zlg_Empty_Diagnostics_And_Reset_Do_Not_Confirm_Recovery()
    {
        using var bus = ZlgCan.Open(ZlgDeviceType.ZCAN_USBCAN2, 0, 0, cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
        ZLGCAN.SetErrorInfo(bus.Handle, 1, (uint)ZlgErrorFlag.BusOff);
        bus.BusState.Should().Be(BusState.BusOff);
        ZLGCAN.SetErrorInfo(bus.Handle, 0, 0);
        bus.BusState.Should().Be(BusState.Unknown);
        ZLGCAN.SetErrorInfo(bus.Handle, 1, 0);
        bus.Reset();
        bus.BusState.Should().Be(BusState.Unknown);
    }

    [Theory]
    [InlineData(PcanStatus.OK, BusState.ErrActive)]
    [InlineData(PcanStatus.BusLight, BusState.ErrWarning)]
    [InlineData(PcanStatus.BusWarning, BusState.ErrWarning)]
    [InlineData(PcanStatus.BusLight | PcanStatus.BusWarning, BusState.ErrWarning)]
    [InlineData(PcanStatus.BusPassive, BusState.ErrPassive)]
    [InlineData(PcanStatus.BusOff, BusState.BusOff)]
    [InlineData(PcanStatus.AnyBusError, BusState.BusOff)]
    [InlineData(PcanStatus.BusPassive | PcanStatus.BusWarning, BusState.ErrPassive)]
    [InlineData(PcanStatus.BusPassive | PcanStatus.BusLight, BusState.ErrPassive)]
    [InlineData(PcanStatus.BusOff | PcanStatus.BusLight, BusState.BusOff)]
    [InlineData(PcanStatus.Overrun, BusState.Unknown)]
    [InlineData(PcanStatus.ReceiveQueueEmpty, BusState.Unknown)]
    [InlineData(PcanStatus.ReceiveQueueOverrun, BusState.Unknown)]
    [InlineData(PcanStatus.TransmitBufferFull, BusState.Unknown)]
    [InlineData(PcanStatus.TransmitQueueFull, BusState.Unknown)]
    [InlineData(PcanStatus.IllegalHardwareHandle, BusState.Unknown)]
    [InlineData(PcanStatus.IllegalHandle, BusState.Unknown)]
    [InlineData(PcanStatus.Initialize, BusState.Unknown)]
    [InlineData(UnrecognizedPcanStatus, BusState.Unknown)]
    [InlineData(PcanStatus.BusOff | PcanStatus.TransmitBufferFull, BusState.BusOff)]
    public void Pcan_Distinguishes_Controller_State_From_Unavailable_Status(PcanStatus native, BusState expected)
    {
        using var bus = Pcan.Open("PCAN_USBBUS1", cfg => cfg.Baud(500_000));
        // Inject native return values to test mapping, not channel lifecycle. Initialize
        // would normally be returned only for an uninitialized channel.
        Api.SetStatus(PcanChannel.Usb01, native);

        bus.BusState.Should().Be(expected);
    }

    [Theory]
    [InlineData(Canlib.canStatus.canOK, Canlib.canSTAT_ERROR_ACTIVE, BusState.ErrActive)]
    [InlineData(Canlib.canStatus.canOK, Canlib.canSTAT_ERROR_WARNING, BusState.ErrWarning)]
    [InlineData(Canlib.canStatus.canOK, Canlib.canSTAT_ERROR_PASSIVE, BusState.ErrPassive)]
    [InlineData(Canlib.canStatus.canOK, Canlib.canSTAT_BUS_OFF, BusState.BusOff)]
    [InlineData(Canlib.canStatus.canOK, Canlib.canSTAT_BUS_OFF | Canlib.canSTAT_ERROR_PASSIVE | Canlib.canSTAT_ERROR_WARNING | Canlib.canSTAT_ERROR_ACTIVE, BusState.BusOff)]
    [InlineData(Canlib.canStatus.canOK, Canlib.canSTAT_ERROR_PASSIVE | Canlib.canSTAT_ERROR_WARNING | Canlib.canSTAT_ERROR_ACTIVE, BusState.ErrPassive)]
    [InlineData(Canlib.canStatus.canOK, 0, BusState.Unknown)]
    [InlineData(Canlib.canStatus.canOK, UnrecognizedKvaserStatus, BusState.Unknown)]
    [InlineData(Canlib.canStatus.canERR_DRIVER, 0, BusState.Unknown)]
    [InlineData(Canlib.canStatus.canERR_NOCARD, Canlib.canSTAT_ERROR_ACTIVE, BusState.Unknown)]
    public void Kvaser_Rejects_Failed_Queries_Even_With_Active_Output_Flags(Canlib.canStatus result, int flags, BusState expected)
    {
        using var bus = Kvaser.Open(0, cfg => cfg.Baud(500_000));
        Canlib.SetStatus(bus.Handle, result, flags);

        bus.BusState.Should().Be(expected);
    }

    [Fact]
    public void Pcan_Query_Error_Does_Not_Pretend_To_Be_Recovery()
    {
        using var bus = Pcan.Open("PCAN_USBBUS1", cfg => cfg.Baud(500_000));
        Api.SetStatus(PcanChannel.Usb01, PcanStatus.BusOff);
        bus.BusState.Should().Be(BusState.BusOff);
        Api.SetStatus(PcanChannel.Usb01, UnrecognizedPcanStatus);
        bus.BusState.Should().Be(BusState.Unknown);
        Api.SetStatus(PcanChannel.Usb01, PcanStatus.OK);
        bus.BusState.Should().Be(BusState.ErrActive);
    }

    [Fact]
    public void Kvaser_Query_Error_Does_Not_Pretend_To_Be_Recovery()
    {
        using var bus = Kvaser.Open(0, cfg => cfg.Baud(500_000));
        Canlib.SetStatus(bus.Handle, Canlib.canStatus.canOK, Canlib.canSTAT_BUS_OFF);
        bus.BusState.Should().Be(BusState.BusOff);
        Canlib.SetStatus(bus.Handle, Canlib.canStatus.canERR_DRIVER, Canlib.canSTAT_ERROR_ACTIVE);
        bus.BusState.Should().Be(BusState.Unknown);
        Canlib.SetStatus(bus.Handle, Canlib.canStatus.canOK, Canlib.canSTAT_ERROR_ACTIVE);
        bus.BusState.Should().Be(BusState.ErrActive);
    }
}
#endif
