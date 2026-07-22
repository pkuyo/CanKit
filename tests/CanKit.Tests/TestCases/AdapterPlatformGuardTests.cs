using System;
using System.Runtime.InteropServices;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// NFR-005: vendor adapters must fail with a clear <see cref="PlatformNotSupportedException"/>
/// on platforms their native SDK does not support, instead of leaking a raw
/// <see cref="DllNotFoundException"/> from the first P/Invoke call. The tests return early on
/// platforms where the adapter is supported (there a real driver/device would be required, so
/// the guard must not fire). The fixture pins TestCaseProvider's static ctor, which force-loads
/// the vendor adapter assemblies so their endpoints are registered in this test host.
/// </summary>
public class AdapterPlatformGuardTests : IClassFixture<TestCaseProvider>
{
    [Theory]
    [InlineData("kvaser://0")]
    [InlineData("vector://XL/0")]
    [InlineData("controlcan://VCI_USBCAN2?index=0#ch0")]
    public void Windows_Only_Adapters_Throw_Clear_Pns_On_Unsupported_Platforms(string endpoint)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // adapter is supported here; the guard must not fire
        }

        Action act = () => CanBus.Open(endpoint);
        act.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void Pcan_Throws_Clear_Pns_Outside_Windows_And_Linux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return; // PCAN-Basic is available on both; the guard must not fire there
        }

        Action act = () => CanBus.Open("pcan://PCAN_USBBUS1");
        act.Should().Throw<PlatformNotSupportedException>();
    }
}
