using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.Kvaser.Exceptions;
using CanKit.Core;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Linux CANlib interop: <c>CanBus.Open("kvaser://0")</c> must not be rejected as an
/// unsupported OS. When <c>libcanlib.so</c> is absent the native load is wrapped;
/// when it is present, device/channel errors are out of scope. Fake builds never
/// load the vendor library.
/// </summary>
public class KvaserLinuxInteropTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void Open_On_Linux_Does_Not_Throw_PlatformNotSupportedException()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        Exception? caught = null;
        try
        {
            using var bus = CanBus.Open("kvaser://0");
        }
        catch (PlatformNotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        if (caught is CanNativeCallException native
            && native.ErrorCode == CanKitErrorCode.NativeLibraryNotFound)
        {
            native.Should().BeOfType<KvaserCanException>();
            native.Message.Should().Contain("libcanlib.so");
        }
    }

#if !FAKE
    [Fact]
    public void Open_Throws_PlatformNotSupported_Outside_Windows_And_Linux()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        Action act = () => CanBus.Open("kvaser://0");
        act.Should().Throw<PlatformNotSupportedException>()
            .Which.Message.Should().Contain("Kvaser")
            .And.Contain("canlib32")
            .And.Contain("libcanlib.so")
            .And.NotContain("Windows-only");
    }
#endif
}
