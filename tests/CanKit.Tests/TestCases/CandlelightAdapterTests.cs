using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI.Common;
using CanKit.Abstractions.SPI.Registry.Core.Endpoints;
using CanKit.Adapter.Candlelight;
using CanKit.Adapter.Candlelight.Native;
using CanKit.Core;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class CandlelightAdapterTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void Native_Layouts_Match_The_Included_Wrapper()
    {
        Marshal.SizeOf<CandleNative.Frame>().Should().Be(24);
        Marshal.OffsetOf<CandleNative.Frame>(nameof(CandleNative.Frame.CanId)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<CandleNative.Frame>(nameof(CandleNative.Frame.Data)).ToInt32().Should().Be(12);
        Marshal.OffsetOf<CandleNative.Frame>(nameof(CandleNative.Frame.TimestampMicroseconds)).ToInt32().Should().Be(20);
        Marshal.SizeOf<CandleNative.Capabilities>().Should().Be(40);
        Marshal.SizeOf<CandleNative.BitTiming>().Should().Be(20);
    }

    [Fact]
    public void Classic_Frame_Conversion_Preserves_Id_Data_And_Flags()
    {
        var original = CanFrame.Classic(0x123456, new byte[] { 1, 2, 3 }, isExtendedFrame: true);
        var native = CandleFrameConverter.ToNative(original, 1);
        native.CanId.Should().Be(0x80123456);
        native.Channel.Should().Be(1);
        native.Dlc.Should().Be(3);
        var received = CandleFrameConverter.FromNative(native);
        received.ID.Should().Be(original.ID);
        received.IsExtendedFrame.Should().BeTrue();
        received.Data.ToArray().Should().Equal(1, 2, 3);

        var remote = CandleFrameConverter.ToNative(CanFrame.Classic(0x123, isRemoteFrame: true), 0);
        remote.CanId.Should().Be(0x40000123);
        CandleFrameConverter.FromNative(remote).IsRemoteFrame.Should().BeTrue();
        native.Dlc = 9;
        Action invalid = () => CandleFrameConverter.FromNative(native);
        invalid.Should().Throw<InvalidOperationException>();
        Action fd = () => CandleFrameConverter.ToNative(CanFrame.Fd(0x123), 0);
        fd.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Timing_Uses_Requested_Bitrate_And_Sample_Point()
    {
        var timing = CandleTiming.Calculate(FakeCandleApi.DefaultCapabilities, 500_000, 875);
        var quanta = 1 + timing.PropSeg + timing.PhaseSeg1 + timing.PhaseSeg2;
        (16_000_000U / (timing.Brp * quanta)).Should().Be(500_000);
        (1000U * (1 + timing.PropSeg + timing.PhaseSeg1) / quanta).Should().Be(875);
        Action unsupported = () => CandleTiming.Calculate(FakeCandleApi.DefaultCapabilities, 123_457);
        unsupported.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Endpoint_Is_Registered_And_Parses_Device_And_Channel()
    {
        var prepared = CandlelightEndpoint.Prepare(CanEndpoint.Parse("candlelight://3#ch1"), null);
        ((CandlelightDeviceOptions)prepared.DeviceOptions).DeviceIndex.Should().Be(3);
        prepared.BusOptions.ChannelIndex.Should().Be(1);
        CanBus.QueryCapabilities("candlelight://0/0").Features.Should().HaveFlag(CanFeature.CanClassic);
        Action invalid = () => CandlelightEndpoint.Prepare(CanEndpoint.Parse("candlelight://bad/0"), null);
        invalid.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task Shared_Device_Routes_Channels_And_Closes_Only_After_Last_Lease()
    {
        var api = new FakeCandleApi();
        var provider = new CandlelightProvider();
        using var firstDevice = new CandlelightDevice(new CandlelightDeviceOptions(provider), api);
        using var secondDevice = new CandlelightDevice(new CandlelightDeviceOptions(provider), api);
        using var first = NewBus(firstDevice, provider, 0);
        using var second = NewBus(secondDevice, provider, 1);
        api.OpenCount.Should().Be(1);
        Action duplicate = () => NewBus(firstDevice, provider, 0);
        duplicate.Should().Throw<InvalidOperationException>();

        api.Push(Frame(1, 0x321, 123));
        var received = await second.ReceiveAsync(1, 2000);
        received.Should().ContainSingle();
        received[0].CanFrame.ID.Should().Be(0x321);
        received[0].ReceiveTimestamp.Should().Be(TimeSpan.FromTicks(1230));
        received[0].IsEcho.Should().BeFalse();
        (await first.ReceiveAsync(1, 0)).Should().BeEmpty();

        first.Transmit(CanFrame.Classic(0x456, new byte[] { 9 })).Should().Be(1);
        api.Sent.TryDequeue(out var sent).Should().BeTrue();
        sent.CanId.Should().Be(0x456);
        sent.Channel.Should().Be(0);

        firstDevice.Dispose();
        secondDevice.Dispose();
        first.Dispose();
        api.CloseCount.Should().Be(0);
        api.Push(Frame(1, 0x322));
        (await second.ReceiveAsync(1, 2000)).Should().ContainSingle().Which.CanFrame.ID.Should().Be(0x322);
        second.Dispose();
        api.StopCount.Should().Be(2);
        api.CloseCount.Should().Be(1);
        api.FreeDeviceCount.Should().Be(1);
    }

    [Fact]
    public async Task Echo_Confirmations_Are_Marked_And_Normal_Mode_Suppresses_Them()
    {
        var api = new FakeCandleApi();
        var provider = new CandlelightProvider();
        using var device = new CandlelightDevice(new CandlelightDeviceOptions(provider), api);
        using var normal = NewBus(device, provider, 0);
        using var echo = NewBus(device, provider, 1, ChannelWorkMode.Echo);
        var confirmation = Frame(0, 0x100);
        confirmation.EchoId = 0;
        api.Push(confirmation);
        confirmation.Channel = 1;
        api.Push(confirmation);
        var result = await echo.ReceiveAsync(1, 2000);
        result.Should().ContainSingle().Which.IsEcho.Should().BeTrue();
        (await normal.ReceiveAsync(1, 50)).Should().BeEmpty();
    }

    [Fact]
    public async Task Received_Frames_Use_The_Configured_Allocator()
    {
        var api = new FakeCandleApi();
        var provider = new CandlelightProvider();
        var allocator = new CountingAllocator();
        using var device = new CandlelightDevice(new CandlelightDeviceOptions(provider), api);
        var options = new CandlelightBusOptions(provider) { BufferAllocator = allocator };
        using var bus = new CandlelightBus(device, options, new CandlelightTransceiver());
        api.Push(Frame(0, 0x321));
        var result = await bus.ReceiveAsync(1, 2000);
        result.Should().ContainSingle();
        allocator.Rents.Should().Be(1);
        allocator.Returns.Should().Be(0);
        result[0].CanFrame.Dispose();
        allocator.Returns.Should().Be(1);
    }

    [Fact]
    public async Task Reader_Failure_Faults_The_Bus_And_Unblocks_Receivers()
    {
        var api = new FakeCandleApi();
        var provider = new CandlelightProvider();
        using var device = new CandlelightDevice(new CandlelightDeviceOptions(provider), api);
        using var bus = NewBus(device, provider, 0);
        var fault = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        bus.FaultOccurred += (_, error) => fault.TrySetResult(error);
        var receive = bus.ReceiveAsync(1, -1);
        api.FailRead = true;
        (await Task.WhenAny(fault.Task, Task.Delay(3000))).Should().Be(fault.Task);
        (await fault.Task).Message.Should().Contain("read frame");
        (await Task.WhenAny(receive, Task.Delay(3000))).Should().Be(receive);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await receive);
        Action transmit = () => bus.Transmit(CanFrame.Classic(0x123));
        transmit.Should().Throw<InvalidOperationException>().WithMessage("*reader has stopped*");
    }

    [Fact]
    public async Task Disposing_Bus_Unblocks_A_Pending_Receiver()
    {
        var api = new FakeCandleApi();
        var provider = new CandlelightProvider();
        using var device = new CandlelightDevice(new CandlelightDeviceOptions(provider), api);
        using var bus = NewBus(device, provider, 0);
        var receive = bus.ReceiveAsync(1, -1);
        bus.Dispose();
        (await Task.WhenAny(receive, Task.Delay(3000))).Should().Be(receive);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await receive);
    }

    [Fact]
    public void Failed_Open_And_Channel_Start_Release_Native_Resources()
    {
        var provider = new CandlelightProvider();
        var openApi = new FakeCandleApi { FailOpen = true };
        Action open = () => new CandlelightDevice(new CandlelightDeviceOptions(provider), openApi);
        open.Should().Throw<InvalidOperationException>();
        openApi.FreeListCount.Should().Be(1);
        openApi.FreeDeviceCount.Should().Be(1);
        openApi.CloseCount.Should().Be(0);

        var startApi = new FakeCandleApi { FailStart = true };
        using (var device = new CandlelightDevice(new CandlelightDeviceOptions(provider), startApi))
        {
            Action start = () => NewBus(device, provider, 0);
            start.Should().Throw<InvalidOperationException>();
        }
        startApi.StopCount.Should().Be(0);
        startApi.CloseCount.Should().Be(1);
        startApi.FreeDeviceCount.Should().Be(1);
    }

    private static CandlelightBus NewBus(CandlelightDevice device, CandlelightProvider provider, int channel, ChannelWorkMode mode = ChannelWorkMode.Normal)
    {
        var options = new CandlelightBusOptions(provider) { ChannelIndex = channel, WorkMode = mode };
        return new CandlelightBus(device, options, new CandlelightTransceiver());
    }

    private static CandleNative.Frame Frame(byte channel, uint id, uint timestamp = 0) => new()
    {
        EchoId = uint.MaxValue,
        CanId = id,
        Channel = channel,
        Dlc = 1,
        Data = new byte[] { 42, 0, 0, 0, 0, 0, 0, 0 },
        TimestampMicroseconds = timestamp
    };

    private sealed class CountingAllocator : IBufferAllocator
    {
        public int Rents { get; private set; }
        public int Returns { get; private set; }
        public bool FrameNeedDispose => true;
        public IMemoryOwner<byte> Rent(int length, bool zeroFill = false)
        {
            Rents++;
            return new Owner(this, length);
        }

        private sealed class Owner(CountingAllocator allocator, int length) : IMemoryOwner<byte>
        {
            public Memory<byte> Memory { get; } = new byte[length];
            public void Dispose() => allocator.Returns++;
        }
    }

    private sealed class FakeCandleApi : ICandleApi
    {
        internal static CandleNative.Capabilities DefaultCapabilities => new()
        {
            Feature = 0x1019, Clock = 16_000_000, Tseg1Min = 1, Tseg1Max = 16,
            Tseg2Min = 1, Tseg2Max = 8, SjwMax = 4, BrpMin = 1, BrpMax = 1024, BrpIncrement = 1
        };
        private readonly ConcurrentQueue<CandleNative.Frame> _frames = new();
        private int _error = CandleNative.ReadTimeout;
        public readonly ConcurrentQueue<CandleNative.Frame> Sent = new();
        public bool FailOpen { get; set; }
        public bool FailStart { get; set; }
        public volatile bool FailRead;
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public int FreeDeviceCount { get; private set; }
        public int FreeListCount { get; private set; }
        public int StopCount { get; private set; }
        public void Push(CandleNative.Frame frame) => _frames.Enqueue(frame);
        public bool Scan(out IntPtr list) { list = (IntPtr)1; return true; }
        public bool FreeList(IntPtr list) { FreeListCount++; return true; }
        public bool ListLength(IntPtr list, out byte length) { length = 1; return true; }
        public bool GetDevice(IntPtr list, byte index, out IntPtr device) { device = (IntPtr)2; return true; }
        public bool Open(IntPtr device) { OpenCount++; if (FailOpen) _error = 17; return !FailOpen; }
        public bool Close(IntPtr device) { CloseCount++; return true; }
        public bool FreeDevice(IntPtr device) { FreeDeviceCount++; return true; }
        public bool ChannelCount(IntPtr device, out byte count) { count = 2; return true; }
        public bool GetCapabilities(IntPtr device, byte channel, out CandleNative.Capabilities capabilities) { capabilities = DefaultCapabilities; return true; }
        public bool SetTiming(IntPtr device, byte channel, ref CandleNative.BitTiming timing) => true;
        public bool Start(IntPtr device, byte channel, CandleNative.Mode mode) { if (FailStart) _error = 17; return !FailStart; }
        public bool Stop(IntPtr device, byte channel) { StopCount++; return true; }
        public bool Send(IntPtr device, byte channel, ref CandleNative.Frame frame) { Sent.Enqueue(frame); return true; }
        public bool Read(IntPtr device, out CandleNative.Frame frame, uint timeoutMilliseconds)
        {
            if (FailRead) { frame = default; _error = 17; return false; }
            if (_frames.TryDequeue(out frame)) return true;
            Thread.Sleep(1);
            _error = CandleNative.ReadTimeout;
            return false;
        }
        public int LastError(IntPtr device) => _error;
    }
}
