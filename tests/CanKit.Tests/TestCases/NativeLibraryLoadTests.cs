using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.ControlCAN.Diagnostics;
using CanKit.Adapter.Kvaser.Exceptions;
using CanKit.Adapter.PCAN.Exceptions;
using CanKit.Adapter.ZLG.Exceptions;
using CanKit.Core;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class NativeLibraryLoadTests
{
    [Fact]
    public void IsFailure_Detects_DllNotFound_And_BadImage()
    {
        NativeLibraryLoad.IsFailure(new DllNotFoundException("missing")).Should().BeTrue();
        NativeLibraryLoad.IsFailure(new BadImageFormatException("bad")).Should().BeTrue();
        NativeLibraryLoad.IsFailure(new InvalidOperationException("other")).Should().BeFalse();
    }

    [Fact]
    public void IsFailure_Walks_Inner_Exceptions()
    {
        var inner = new DllNotFoundException("zlgcan.dll");
        NativeLibraryLoad.IsFailure(new InvalidOperationException("wrap", inner)).Should().BeTrue();
    }

    [Fact]
    public void IsFailure_Does_Not_Rewrap_NativeLibraryNotFound()
    {
        var wrapped = ZlgCanException.NativeLibraryNotFound("Open", new DllNotFoundException("zlgcan.dll"));
        NativeLibraryLoad.IsFailure(wrapped).Should().BeFalse();
    }

    [Fact]
    public void FormatMessage_Names_Library_Vendor_And_Bitness()
    {
        var bitness = Environment.Is64BitProcess ? "64-bit" : "32-bit";
        var message = NativeLibraryLoad.FormatMessage("zlgcan.dll", "ZLG CAN driver");

        message.Should().Contain("zlgcan.dll");
        message.Should().Contain("Install the ZLG CAN driver");
        message.Should().Contain("DLL search path");
        message.Should().Contain(bitness);
    }

    [Fact]
    public void FormatMessage_Mentions_Bitness_Mismatch_For_BadImage()
    {
        var message = NativeLibraryLoad.FormatMessage(
            "PCANBasic",
            "Peak PCAN-Basic runtime",
            new BadImageFormatException("wrong machine"));

        message.Should().Contain("invalid or does not match");
    }

    [Fact]
    public void Adapter_Factories_Preserve_InnerException_And_ErrorCode()
    {
        var library = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "libcanlib.so"
            : "canlib32";
        var inner = new DllNotFoundException(library);
        AssertWrapped(ZlgCanException.NativeLibraryNotFound("Open", new DllNotFoundException("zlgcan.dll")), "zlgcan.dll", "ZLG CAN driver");
        AssertWrapped(KvaserCanException.NativeLibraryNotFound("Open", inner), library, "Kvaser CANlib");
        AssertWrapped(ControlCanException.NativeLibraryNotFound("Open", new DllNotFoundException("controlcan")), "controlcan", "ControlCAN");
        AssertWrapped(
            PcanCanException.NativeLibraryNotFound("Open", "PCANBasic", "Peak PCAN-Basic runtime", new DllNotFoundException("PCANBasic")),
            "PCANBasic",
            "Peak PCAN-Basic runtime");
        AssertWrapped(
            PcanCanException.NativeLibraryNotFound(
                "Open",
                "PCAN-ISO-TP.dll",
                "Peak PCAN-ISO-TP runtime",
                new DllNotFoundException("PCAN-ISO-TP.dll")),
            "PCAN-ISO-TP.dll",
            "Peak PCAN-ISO-TP runtime");
    }

    private static void AssertWrapped(CanNativeCallException ex, string library, string vendor)
    {
        ex.ErrorCode.Should().Be(CanKitErrorCode.NativeLibraryNotFound);
        ex.InnerException.Should().BeAssignableTo<DllNotFoundException>();
        ex.Message.Should().Contain(library);
        ex.Message.Should().Contain(vendor);
        ex.Message.Should().Contain("Install");
        ex.Operation.Should().Be("Open");
        ex.NativeErrorCode.Should().BeNull();
    }
}

#if !FAKE
public class AdapterMissingNativeLibraryTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [InlineData("zlg://USBCANFD-200U?index=0#ch0", "zlgcan.dll", typeof(ZlgCanException))]
    [InlineData("controlcan://VCI_USBCAN2?index=0#ch0", "controlcan", typeof(ControlCanException))]
    public void Open_Wraps_Missing_Vendor_Library(string endpoint, string library, Type exceptionType)
    {
        AssertOpenWrapsOrLibPresent(endpoint, library, exceptionType);
    }

    [Fact]
    public void Open_Kvaser_Wraps_Missing_Canlib()
    {
        var library = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "libcanlib.so"
            : "canlib32";
        AssertOpenWrapsOrLibPresent("kvaser://0", library, typeof(KvaserCanException));
    }

    [Fact]
    public void Open_Vector_Wraps_Missing_Vxlapi()
    {
        var library = Environment.Is64BitProcess ? "vxlapi64" : "vxlapi";
        AssertOpenWrapsOrLibPresent("vector://XL/0", library, typeof(CanNativeCallException));
    }

    [Fact]
    public void Open_Pcan_Wraps_Missing_PcanBasic()
    {
        var library = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PCANBasic" : "libpcanbasic";
        AssertOpenWrapsOrLibPresent("pcan://PCAN_USBBUS1", library, typeof(PcanCanException));
    }

    private static void AssertOpenWrapsOrLibPresent(string endpoint, string library, Type exceptionType)
    {
        try
        {
            using var bus = CanBus.Open(endpoint);
        }
        catch (Exception ex) when (exceptionType.IsInstanceOfType(ex) && ex is CanNativeCallException)
        {
            var native = (CanNativeCallException)ex;
            native.ErrorCode.Should().Be(CanKitErrorCode.NativeLibraryNotFound);
            native.Message.Should().Contain(library);
            native.Message.Should().Contain("Install");
            native.InnerException.Should().NotBeNull();
            NativeLibraryLoad.IsFailure(native.InnerException!).Should().BeTrue();
            return;
        }
        catch (CanFactoryException)
        {
            throw;
        }
        catch (DllNotFoundException)
        {
            throw;
        }
        catch (BadImageFormatException)
        {
            throw;
        }
        catch (Exception)
        {
            // Native library loaded; device/channel errors are out of scope for this test.
        }
    }
}
#endif

#if FAKE
public class AdapterMissingNativeLibraryFakeTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [InlineData("zlg://ZCAN_USBCAN2?index=0#ch0")]
    [InlineData("kvaser://0")]
    [InlineData("vector://virtual/0")]
    [InlineData("controlcan://USBCAN2?index=0#ch0")]
    [InlineData("pcan://PCAN_USBBUS1")]
    public void Fake_Open_Does_Not_Report_NativeLibraryNotFound(string endpoint)
    {
        try
        {
            using var bus = CanBus.Open(endpoint);
            bus.Should().NotBeNull();
        }
        catch (Exception ex)
        {
            ex.Should().NotBeOfType<DllNotFoundException>();
            ex.Should().NotBeOfType<BadImageFormatException>();
            if (ex is CanNativeCallException native)
                native.ErrorCode.Should().NotBe(CanKitErrorCode.NativeLibraryNotFound);
        }
    }
}
#endif
