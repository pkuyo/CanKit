using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Diagnostics;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Registry;
using Pkuyo.CanKit.Net.Tests.Fakes;

namespace Pkuyo.CanKit.Net.Tests;

public class RegistryAndLoggingTests
{
    [Fact]
    public void Registry_Resolves_TestProvider()
    {
        var provider = CanRegistry.Registry.Resolve(TestDeviceTypes.Test);
        Assert.NotNull(provider);
        Assert.Equal(TestDeviceTypes.Test, provider.DeviceType);
    }

    [Fact]
    public void Exceptions_Are_Logged_On_Creation()
    {
        var entries = new List<CanKitLogEntry>();
        CanKitLogger.Configure(entries.Add);

        try
        {
            // Trigger an exception creation in library code
            var ex = Assert.Throws<CanDeviceNotOpenException>(() =>
            {
                var provider = new TestModelProvider();
                var device = new TestDevice(new TestDeviceOptions { Provider = provider });
                using var session = new Pkuyo.CanKit.Net.Core.CanSession<TestDevice, TestChannel>(device, provider);
                session.CreateChannel(0, cfg => cfg.Baud(500_000));
            });

            Assert.NotNull(ex);
            Assert.NotEmpty(entries);
            Assert.Contains(entries, e => e is { Level: CanKitLogLevel.Error, Exception: CanDeviceNotOpenException });
        }
        finally
        {
            // reset to no-op to avoid leaking handler to other tests
            CanKitLogger.Configure(_ => { });
        }
    }
    
    [Fact]
    public void Logger_Delegates_Debug_Info_Warn_To_Handler()
    {
        var entries = new List<CanKitLogEntry>();
        try
        {
            CanKitLogger.Configure(entries.Add);
            CanKitLogger.LogDebug("dbg");
            CanKitLogger.LogInformation("info");
            CanKitLogger.LogWarning("warn");

            Assert.Equal(3, entries.Count);
            Assert.Contains(entries, e => e.Level == CanKitLogLevel.Debug && e.Message.Contains("dbg"));
            Assert.Contains(entries, e => e.Level == CanKitLogLevel.Information && e.Message.Contains("info"));
            Assert.Contains(entries, e => e.Level == CanKitLogLevel.Warning && e.Message.Contains("warn"));
        }
        finally
        {
            CanKitLogger.Configure(_ => { });
        }
    }
}

