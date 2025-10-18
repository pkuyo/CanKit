using System;
using System.Collections.Generic;
using CanKit.Adapter.ZLG.Options;
using CanKit.Adapter.ZLG.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Tests;

namespace CanKit.Adapter.ZLG.Tests;

public class TestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } =
        [
            ("zlg://ZCAN_USBCAN2?index=0#ch0", "zlg://ZCAN_USBCAN2?index=0#ch1", false),
            ("zlg://ZCAN_USBCANFD_200U?index=0#ch0", "zlg://ZCAN_USBCANFD_200U?index=0#ch1", true),
            ("zlg://ZCAN_PCIE_CANFD_200U_M2?index=0#ch0", "zlg://ZCAN_PCIE_CANFD_200U_M2?index=0#ch1", true),
        ];

    //ZLG没有给出明确的ACC定义方式，故暂不测试
    public IEnumerable<(ITestDataProvider.FilterMask[] filters, ITestDataProvider.FilterFrame[] frames, int exceptResult
        )> MaskFilterCases
    { get; } = [];

    public IEnumerable<(ICanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; } = [];

    public IEnumerable<(ICanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; } = [];
    public (int aBit, int dBit)? BaudRate { get; } = ((int)ZlgBaudRate.ZLG_1M, (int)ZlgDataDaudRate.ZLG_5M);

    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; } = (cfg) =>
    {
        var zlg = (ZlgBusInitConfigurator)cfg;
        zlg.InternalRes(true);
    };
}
