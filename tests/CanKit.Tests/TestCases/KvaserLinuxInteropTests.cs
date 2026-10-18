using System;
using System.Runtime.InteropServices;
using CanKit.Adapter.Kvaser.Exceptions;
using CanKit.Core;
using CanKit.Core.Exceptions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Linux CANlib interop: <c>CanBus.Open("kvaser://0")</c> must use <c>libcanlib.so</c>
/// rather than the Windows <c>canlib32</c> ABI. When the library is absent the native
/// load is wrapped; when it is present, device/channel errors are out of scope.
/// Fake builds never load the vendor library.
/// </summary>
public class KvaserLinuxInteropTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void Open_On_Linux_Uses_Libcanlib_Not_Windows_Abi()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        Exception? caught = null;
        try
        {
            using var bus = CanBus.Open("kvaser://0");
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
            native.Message.Should().NotContain("canlib32");
        }
    }
}
