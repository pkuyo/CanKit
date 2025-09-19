using System;
using Pkuyo.CanKit.Net;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Registry;
using Pkuyo.CanKit.Net.Tests.Fakes;

namespace Pkuyo.CanKit.Net.Tests;

public class CanOpenErrorPathsTests
{
    [Fact]
    public void Open_DeviceOption_Mismatch_Throws()
    {
        Assert.Throws<CanOptionTypeMismatchException>(() =>
            Can.Open<OtherDevice, ICanChannel, DummyDeviceOptions, IDeviceInitOptionsConfigurator>(TestDeviceTypes.Test));
    }

    [Fact]
    public void Open_DeviceConfigurator_Mismatch_Throws()
    {
        Assert.Throws<CanOptionTypeMismatchException>(() =>
            Can.Open<ICanDevice, ICanChannel, IDeviceOptions, DummyDeviceInitCfg>(TestDeviceTypes.Test));
    }

    [Fact]
    public void Open_Factory_CreateDevice_ReturnsNull_Throws()
    {
        Assert.Throws<CanFactoryException>(() =>
            Can.Open<ICanDevice, ICanChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(BrokenDeviceTypes.NullDeviceFactory));
    }

    [Fact]
    public void Open_FactoryDevice_Mismatch_Throws()
    {
        Assert.Throws<CanFactoryDeviceMismatchException>(() =>
            Can.Open<OtherDevice, ICanChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(TestDeviceTypes.Test));
    }

    [Fact]
    public void Registry_Resolve_UnknownDevice_Throws()
    {
        var unknown = DeviceType.Register($"tests-unknown-{Guid.NewGuid():N}", 99999);
        Action act = () => CanRegistry.Registry.Resolve(unknown);
        Assert.Throws<NotSupportedException>(act);
    }

    [Fact]
    public void Registry_Factory_ByAttribute_IsDiscoverable()
    {
        var fac = CanRegistry.Registry.Factory("tests-factory-attribute");
        Assert.NotNull(fac);
    }

    private sealed class CustomSession : CanSession<TestDevice, TestChannel>
    {
        public CustomSession(TestDevice device, ICanModelProvider provider) : base(device, provider) { }
    }

    [Fact]
    public void Open_Uses_Custom_SessionBuilder_And_Opens()
    {
        var session = Can.Open<TestDevice, TestChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(
            TestDeviceTypes.Test,
            configure: null,
            sessionBuilder: (d, p) => new CustomSession(d, p));

        Assert.IsType<CustomSession>(session);
        Assert.True(session.IsDeviceOpen);
        session.Dispose();
    }

    [Fact]
    public void CreateChannel_TransceiverNull_Throws()
    {
        // Open session on provider whose factory returns null transceiver
        var session = Can.Open<ICanDevice, ICanChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(BrokenDeviceTypes.NullTransceiverFactory);
        try
        {
            Assert.True(session.IsDeviceOpen);
            Assert.Throws<CanFactoryException>(() => session.CreateChannel(0, cfg => cfg.Baud(500_000)));
        }
        finally
        {
            session.Dispose();
        }
    }

    [Fact]
    public void CreateChannel_TypeMismatch_Throws()
    {
        var session = Can.Open<TestDevice, OtherChannel, IDeviceOptions, IDeviceInitOptionsConfigurator>(BrokenDeviceTypes.MismatchChannelFactory);
        try
        {
            session.Open();
            Assert.Throws<CanChannelCreationException>(() => session.CreateChannel(0, cfg => cfg.Baud(500_000)));
        }
        finally
        {
            session.Dispose();
        }
    }
}
