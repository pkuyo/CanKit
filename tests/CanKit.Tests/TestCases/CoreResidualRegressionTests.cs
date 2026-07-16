using System;
using System.Collections.Generic;
using System.Threading;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.Attributes;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Factories;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Core.Diagnostics;
using CanKit.Core.Registry;
using CanKit.Core.Utils;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class CoreResidualRegressionTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void SoftwarePeriodicTx_Completed_Handler_Can_Update_And_Stop()
    {
        var session = $"completed-{Guid.NewGuid():N}";
        using var bus = CanBus.Open($"virtual://{session}/0", cfg => cfg
            .SetProtocolMode(CanProtocolMode.Can20)
            .Baud(TestCaseProvider.AbitRate)
            .SoftwareFeaturesFallBack(CanFeature.All));

        using var completed = new ManualResetEventSlim();
        Exception? handlerException = null;

        using var periodic = bus.TransmitPeriodic(
            CanFrame.Classic(0x321, new byte[] { 1, 2, 3 }),
            new PeriodicTxOptions(TimeSpan.FromMilliseconds(5), 1, fireImmediately: false));

        periodic.Completed += (sender, _) =>
        {
            try
            {
                var tx = (IPeriodicTx)sender!;
                tx.Update(period: TimeSpan.FromMilliseconds(10), repeatCount: 1);
                tx.Stop();
            }
            catch (Exception ex)
            {
                handlerException = ex;
            }
            finally
            {
                completed.Set();
            }
        };

        completed.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("Completed handlers must be able to call back into the handle");
        handlerException.Should().BeNull();
    }

    [Fact]
    public void BitTimingSolver_Skips_Invalid_Small_Ntq_And_Continues_Search()
    {
        var limits = new BitTimingLimits
        {
            NtqMin = 2,
            NtqMax = 8,
            Tseg1Min = 1,
            Tseg1Max = 6,
            Tseg2Min = 1,
            Tseg2Max = 4,
            SjwMin = 1,
            SjwMax = 4,
            PreferLargerNtqWhenTied = false
        };

        var timing = BitTimingSolver.FromSamplePoint(8, 1_000_000, 0.75, limits);

        timing.Ntq.Should().Be(4);
        timing.SamplePointPermille.Should().Be(750);
    }

    [Fact]
    public void CanBus_Open_DeviceType_Disposes_Device_When_Open_Fails_After_CreateDevice()
    {
        OpenFailureRegister.FactoryInstance.Reset();

        Action open = () => CanBus.Open(OpenFailureDeviceType.Value);

        open.Should().Throw<InvalidOperationException>().WithMessage("transceiver failed");
        OpenFailureRegister.FactoryInstance.LastDevice.Should().NotBeNull();
        OpenFailureRegister.FactoryInstance.LastDevice!.Disposed.Should().BeTrue();
    }

    [Fact]
    public void CanBus_Open_DeviceType_Preserves_Open_Exception_When_Dispose_Fails()
    {
        OpenFailureRegister.FactoryInstance.Reset();
        OpenFailureRegister.FactoryInstance.ThrowOnDispose = true;

        Action open = () => CanBus.Open(OpenFailureDeviceType.Value);

        open.Should().Throw<InvalidOperationException>().WithMessage("transceiver failed");
        OpenFailureRegister.FactoryInstance.LastDevice.Should().NotBeNull();
        OpenFailureRegister.FactoryInstance.LastDevice!.Disposed.Should().BeTrue();
    }

    private static class OpenFailureDeviceType
    {
        public static readonly DeviceType Value = DeviceType.Register("CanKit.Tests.OpenFailure");
    }

    [CanRegistryEntry(CanRegistryEntryKind.Adapter, "OpenFailure")]
    public sealed class OpenFailureRegister : ICanRegisterFactory, ICanRegisterProviders
    {
        public static OpenFailureFactory FactoryInstance { get; } = new();

        public (string FactoryId, ICanFactory Factory) Factory => ("CanKit.Tests.OpenFailure", FactoryInstance);

        public IEnumerable<ICanModelProvider> Providers => new[] { new OpenFailureProvider() };
    }

    public sealed class OpenFailureFactory : ICanFactory
    {
        public OpenFailureDevice? LastDevice { get; private set; }
        public bool ThrowOnDispose { get; set; }

        public void Reset()
        {
            LastDevice = null;
            ThrowOnDispose = false;
        }

        public ICanDevice CreateDevice(IDeviceOptions options)
        {
            LastDevice = new OpenFailureDevice((OpenFailureDeviceOptions)options, ThrowOnDispose);
            return LastDevice;
        }

        public ICanBus CreateBus(ICanDevice device, IBusOptions options, ITransceiver transceiver, ICanModelProvider provider)
        {
            throw new NotSupportedException("CreateBus should not be reached.");
        }

        public ITransceiver CreateTransceivers(IDeviceRTOptionsConfigurator deviceOptions, IBusInitOptionsConfigurator busOptions)
        {
            throw new InvalidOperationException("transceiver failed");
        }

        public bool Support(DeviceType deviceType) => deviceType.Equals(OpenFailureDeviceType.Value);
    }

    private sealed class OpenFailureProvider : ICanModelProvider
    {
        public DeviceType DeviceType => OpenFailureDeviceType.Value;
        public CanFeature StaticFeatures => CanFeature.CanClassic;
        public ICanFactory Factory => OpenFailureRegister.FactoryInstance;

        public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
        {
            var options = new OpenFailureDeviceOptions();
            var cfg = new OpenFailureDeviceInitConfigurator();
            cfg.Init(options);
            return (options, cfg);
        }

        public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions()
        {
            var options = new OpenFailureBusOptions();
            var cfg = new OpenFailureBusInitConfigurator();
            cfg.Init(options);
            return (options, cfg);
        }
    }

    public sealed class OpenFailureDevice : ICanDevice<DeviceRTOptionsConfigurator<OpenFailureDeviceOptions>>
    {
        private readonly bool _throwOnDispose;

        public OpenFailureDevice(OpenFailureDeviceOptions options, bool throwOnDispose)
        {
            _throwOnDispose = throwOnDispose;
            Options = new DeviceRTOptionsConfigurator<OpenFailureDeviceOptions>();
            Options.Init(options);
        }

        public bool Disposed { get; private set; }

        public DeviceRTOptionsConfigurator<OpenFailureDeviceOptions> Options { get; }

        IDeviceRTOptionsConfigurator ICanDevice.Options => Options;

        public void Dispose()
        {
            Disposed = true;
            if (_throwOnDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }
    }

    public sealed class OpenFailureDeviceOptions : IDeviceOptions
    {
        public DeviceType DeviceType => OpenFailureDeviceType.Value;
        public CanFeature Features { get; set; } = CanFeature.CanClassic;
    }

    private sealed class OpenFailureDeviceInitConfigurator
        : DeviceInitOptionsConfigurator<OpenFailureDeviceOptions, OpenFailureDeviceInitConfigurator>;

    public sealed class OpenFailureBusOptions : IBusOptions
    {
        public int ChannelIndex { get; set; }
        public string? ChannelName { get; set; } = "open-failure";
        public CanBusTiming BitTiming { get; set; } = CanBusTiming.ClassicDefault();
        public bool InternalResistance { get; set; }
        public ChannelWorkMode WorkMode { get; set; } = ChannelWorkMode.Normal;
        public TxRetryPolicy TxRetryPolicy { get; set; } = TxRetryPolicy.AlwaysRetry;
        public CanProtocolMode ProtocolMode { get; set; } = CanProtocolMode.Can20;
        public ICanFilter Filter { get; set; } = new CanFilter();
        public CanFeature EnabledSoftwareFallback { get; set; }
        public Capability Capabilities { get; set; } = new(CanFeature.CanClassic);
        public bool AllowErrorInfo { get; set; }
        public int AsyncBufferCapacity { get; set; }
        public IBufferAllocator BufferAllocator { get; set; } = new DefaultBufferAllocator();
        public CanExceptionPolicy? ExceptionPolicy { get; set; }
        public CanFeature Features { get; set; } = CanFeature.CanClassic;
    }

    public sealed class OpenFailureBusInitConfigurator
        : BusInitOptionsConfigurator<OpenFailureBusOptions, OpenFailureBusInitConfigurator>;
}
