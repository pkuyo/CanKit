using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Tests;

namespace CanKit.Adapter.PCAN.Tests;

public class TestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } =
        [("pcan://PCAN_USBBUS1", "pcan://PCAN_USBBUS2", true)];

    public IEnumerable<(ITestDataProvider.FilterMask[] filters, ITestDataProvider.FilterFrame[] frames, int exceptResult)> MaskFilterCases { get; } = [];

    public IEnumerable<(ICanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; } = [];

    public IEnumerable<(ICanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; } = [];
    public (int aBit, int dBit)? BaudRate { get; } = null;
    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; } = null;
}

