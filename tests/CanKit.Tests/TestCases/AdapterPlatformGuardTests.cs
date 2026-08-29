#if !FAKE
using System;
using System.Runtime.InteropServices;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Vendor adapters must fail with a clear <see cref="PlatformNotSupportedException"/> on
/// platforms their native SDK does not support, instead of leaking a raw
/// <see cref="DllNotFoundException"/> from the first P/Invoke. Tests return early on
/// platforms where the adapter is supported (a real driver/device would be required there,
/// so the guard must not fire). The fixture pins <see cref="TestCaseProvider"/>'s static
/// constructor, which force-loads the vendor adapter assemblies so their endpoints are
/// registered in this test host.
/// </summary>
public class AdapterPlatformGuardTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [InlineData("kvaser://0", "Kvaser")]
    [InlineData("vector://XL/0", "Vector")]
    [InlineData("controlcan://VCI_USBCAN2?index=0#ch0", "ControlCAN")]
    [InlineData("zlg://USBCANFD-200U?index=0#ch0", "ZLG")]
    public void Windows_Only_Adapters_Throw_Clear_Pns_On_Unsupported_Platforms(
        string endpoint, string adapterName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        Action act = () => CanBus.Open(endpoint);
        act.Should().Throw<PlatformNotSupportedException>()
            .Which.Message.Should().Contain(adapterName);
    }

    [Fact]
    public void Pcan_Throws_Clear_Pns_Outside_Windows_And_Linux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        Action act = () => CanBus.Open("pcan://PCAN_USBBUS1");
        act.Should().Throw<PlatformNotSupportedException>()
            .Which.Message.Should().Contain("PCAN");
    }
}
#endif

#if FAKE
using System;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Under the Fake configuration the platform guards are compiled out so fake-native tests
/// can open vendor endpoints on every OS.
/// </summary>
public class AdapterPlatformGuardFakeTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void Fake_Build_Does_Not_Throw_PlatformNotSupportedException_Off_Windows()
    {
        Action act = () =>
        {
            using var bus = CanBus.Open("kvaser://0");
        };
        act.Should().NotThrow<PlatformNotSupportedException>();
    }
}
#endif
