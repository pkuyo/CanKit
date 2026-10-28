#if FAKE
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.Kvaser;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.PCAN;
using FluentAssertions;
using Peak.Can.Basic;
using Xunit;

namespace CanKit.Tests.TestCases;

public class ControllerBusStateTests
{
    [Theory]
    [InlineData(0u, BusState.ErrActive)]
    [InlineData(8u, BusState.ErrWarning)]
    [InlineData(262144u, BusState.ErrPassive)]
    [InlineData(16u, BusState.BusOff)]
    [InlineData(262168u, BusState.BusOff)]
    [InlineData(262152u, BusState.ErrPassive)]
    [InlineData(1u, BusState.Unknown)]
    [InlineData(128u, BusState.Unknown)]
    [InlineData(0x40000000u, BusState.Unknown)]
    [InlineData(17u, BusState.BusOff)]
    public void Pcan_Distinguishes_Controller_State_From_Unavailable_Status(uint native, BusState expected)
    {
        using var bus = Pcan.Open("PCAN_USBBUS1", cfg => cfg.Baud(500_000));
        Api.SetStatus(PcanChannel.Usb01, (PcanStatus)native);

        bus.BusState.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 8, BusState.ErrActive)]
    [InlineData(0, 4, BusState.ErrWarning)]
    [InlineData(0, 1, BusState.ErrPassive)]
    [InlineData(0, 2, BusState.BusOff)]
    [InlineData(0, 15, BusState.BusOff)]
    [InlineData(0, 13, BusState.ErrPassive)]
    [InlineData(0, 0, BusState.Unknown)]
    [InlineData(0, 0x40000000, BusState.Unknown)]
    [InlineData(-12, 0, BusState.Unknown)]
    [InlineData(-26, 8, BusState.Unknown)]
    public void Kvaser_Rejects_Failed_Queries_Even_With_Active_Output_Flags(int result, int flags, BusState expected)
    {
        using var bus = Kvaser.Open(0, cfg => cfg.Baud(500_000));
        Canlib.SetStatus(bus.Handle, (Canlib.canStatus)result, flags);

        bus.BusState.Should().Be(expected);
    }

    [Fact]
    public void Pcan_Query_Error_Does_Not_Pretend_To_Be_Recovery()
    {
        using var bus = Pcan.Open("PCAN_USBBUS1", cfg => cfg.Baud(500_000));
        Api.SetStatus(PcanChannel.Usb01, PcanStatus.BusOff);
        bus.BusState.Should().Be(BusState.BusOff);
        Api.SetStatus(PcanChannel.Usb01, (PcanStatus)0x40000000);
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
