using System;
using System.Threading;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Exceptions;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.Net.Sample
{
    internal class CanKitSample
    {
        // 快速上手示例：展示如何打开 ZLG 设备、创建收发通道并发送 CAN 报文。
        static void Main(string[] args)
        {
            // 1. 选择设备型号并配置基础参数（设备索引、发送超时时间等）。
            using var can = ZlgDeviceType.ZCAN_USBCAN2
                .Open(cfg =>
                    cfg.DeviceIndex(0)
                        .TxTimeOut(50));

            if (!can.IsDeviceOpen)
            {
                Console.WriteLine("Open Device Failed");
                return;
            }

            // 2. 创建发送通道，配置波特率及协议模式。
            var sendChannel = can.CreateChannel(0, cfg =>
                cfg.Baud(500_000)
                    .SetProtocolMode(CanProtocolMode.Can20));

            // 3. 创建监听通道，演示如何设置验收滤波、工作模式等参数。
            var listenChannel = can.CreateChannel(1, cfg =>
                cfg.Baud(500_000)
                    .AccMask(0X78, 0xFFFFFF87)
                    .SetWorkMode(ChannelWorkMode.ListenOnly)
                    .SetMaskFilterType(ZlgChannelOptions.MaskFilterType.Single)
                    .SetProtocolMode(CanProtocolMode.Can20));

            // 4. 订阅通道事件：收到帧时打印基本信息。
            listenChannel.FrameReceived += (sender, data) =>
            {
                Console.Write($"[{data.SystemTimestamp}] [{data.recvTimestamp / 1000f}ms] 0x{data.CanFrame.ID:X}, {data.CanFrame.Dlc}");
                foreach (var by in data.CanFrame.Data.Span)
                    Console.Write($" {by:X2}");
                Console.WriteLine();
            };

            // 当硬件或驱动上报错误时输出详细诊断信息。
            listenChannel.ErrorOccurred += (sender, frame) =>
            {
                Console.WriteLine(
                    $"[{frame.SystemTimestamp}] Error Code: {((ZlgErrorInfo)frame).ErrorCode}, Error Kind: {frame.Kind}, Direction:{frame.Direction}");
            };

            // 5. 打开通道，使硬件进入工作状态。
            sendChannel.Open();
            listenChannel.Open();

            // 6. 循环发送两帧 CAN 报文，便于观察监听通道的回环效果。
            for (int i = 0; i < 500; i++)
            {
                sendChannel.Transmit(
                    new CanClassicFrame(0x1824080F, new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD]), true),
                    new CanClassicFrame(0x18240801, new ReadOnlyMemory<byte>([0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]), true)
                );

                // 间隔发送，避免淹没总线，也便于观察输出。
                Thread.Sleep(400);
            }
        }
    }
}
