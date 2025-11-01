using System;
using System.Collections.Generic;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.SPI.Tests;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.ControlCAN.Tests;

public class TestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } =
        [
            ("controlcan://USBCAN2?index=0#ch0", "controlcan://USBCAN2?index=0#ch1", false),
        ];

    //TODO: SJA1000d的acccode和accmask太繁琐，未来添加测试
    public IEnumerable<(ITestDataProvider.FilterMask[] filters, ITestDataProvider.FilterFrame[] frames, int exceptResult
        )> MaskFilterCases
    { get; } = [];

    public IEnumerable<(CanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; } = [];

    public IEnumerable<(CanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; } = [];
    public (int aBit, int dBit)? BaudRate { get; }
    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; }
}
