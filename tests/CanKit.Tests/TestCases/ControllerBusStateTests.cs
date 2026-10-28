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
    private const PcanStatus UnrecognizedPcanStatus = (PcanStatus)0x40000000;
    private const int UnrecognizedKvaserStatus = 0x40000000;

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
