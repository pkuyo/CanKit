#if FAKE
using System;
using System.Threading.Tasks;
using CanKit.Adapter.ZLG.Definitions;
using CanKit.Adapter.ZLG.Native;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class ZlgFakeNativeReceiveTests
{
    [Theory]
    [InlineData(NativeReceiveKind.Classic)]
    [InlineData(NativeReceiveKind.Fd)]
    [InlineData(NativeReceiveKind.Merged)]
    public async Task Receive_InfiniteWait_PreservesSentinelAcrossPartialBatch(
        NativeReceiveKind receiveKind)
    {
        var devicePointer = ZLGCAN.ZCAN_OpenDevice(
            ZLGCAN.ZCAN_USBCANFD_200U,
            uint.MaxValue,
            0);
        using var device = new ZlgDeviceHandle(devicePointer);

        var config = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG
        {
            can_type = receiveKind == NativeReceiveKind.Classic ? 0U : 1U,
            config =
            {
                can =
                {
                    acc_mask = uint.MaxValue
                }
            }
        };

        using var rx = ZLGCAN.ZCAN_InitCAN(device, 0, ref config);
        rx.SetDevice(devicePointer);
        ConfigureBitrate(device, 0, receiveKind);
        ZLGCAN.ZCAN_SetValue(device, "0/work_mode", "2").Should().Be(1);
        ZLGCAN.ZCAN_StartCAN(rx).Should().Be(1);

        var receiveStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receiveTask = StartReceive(
            receiveKind,
            device,
            rx,
            receiveStarted);

        var ct = TestContext.Current.CancellationToken;
        await receiveStarted.Task;
        await Task.Delay(50, ct);
        Transmit(rx, receiveKind, 0x101);
        await Task.Delay(50, ct);
        Transmit(rx, receiveKind, 0x102);

        var completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(2), ct));
        completed.Should().Be(receiveTask, "the infinite-wait receive should finish once the batch arrives");
        var received = await receiveTask;
        received.Should().Be(2);
    }

    private static Task<uint> StartReceive(
        NativeReceiveKind receiveKind,
        ZlgDeviceHandle device,
        ZlgChannelHandle channel,
        TaskCompletionSource<object?> receiveStarted)
        => Task.Run(() =>
        {
            receiveStarted.SetResult(null);
            return receiveKind switch
            {
                NativeReceiveKind.Classic => ZLGCAN.ZCAN_Receive(
                    channel,
                    new ZLGCAN.ZCAN_Receive_Data[2],
                    2,
                    -1),
                NativeReceiveKind.Fd => ZLGCAN.ZCAN_ReceiveFD(
                    channel,
                    new ZLGCAN.ZCAN_ReceiveFD_Data[2],
                    2,
                    -1),
                NativeReceiveKind.Merged => ZLGCAN.ZCAN_ReceiveData(
                    device,
                    new ZLGCAN.ZCANDataObj[2],
                    2,
                    -1),
                _ => throw new ArgumentOutOfRangeException(nameof(receiveKind))
            };
        });

    private static void Transmit(
        ZlgChannelHandle channel,
        NativeReceiveKind receiveKind,
        uint id)
    {
        if (receiveKind == NativeReceiveKind.Classic)
        {
            TransmitClassic(channel, id);
            return;
        }

        TransmitFd(channel, id);
    }

    private static unsafe void TransmitClassic(ZlgChannelHandle channel, uint id)
    {
        var frame = new ZLGCAN.ZCAN_Transmit_Data
        {
            frame = new ZLGCAN.can_frame
            {
                can_id = id,
                can_dlc = 0
            }
        };

        ZLGCAN.ZCAN_Transmit(channel, &frame, 1).Should().Be(1);
    }

    private static unsafe void TransmitFd(ZlgChannelHandle channel, uint id)
    {
        var frame = new ZLGCAN.ZCAN_TransmitFD_Data
        {
            frame = new ZLGCAN.canfd_frame
            {
                can_id = id,
                len = 0
            }
        };

        ZLGCAN.ZCAN_TransmitFD(channel, &frame, 1).Should().Be(1);
    }

    private static void ConfigureBitrate(
        ZlgDeviceHandle device,
        int channel,
        NativeReceiveKind receiveKind)
    {
        if (receiveKind == NativeReceiveKind.Classic)
        {
            ZLGCAN.ZCAN_SetValue(device, $"{channel}/baud_rate", "500000")
                .Should().Be(1);
            return;
        }

        ZLGCAN.ZCAN_SetValue(device, $"{channel}/canfd_abit_baud_rate", "500000")
            .Should().Be(1);
        ZLGCAN.ZCAN_SetValue(device, $"{channel}/canfd_dbit_baud_rate", "2000000")
            .Should().Be(1);
    }

    public enum NativeReceiveKind
    {
        Classic,
        Fd,
        Merged
    }
}
#endif
