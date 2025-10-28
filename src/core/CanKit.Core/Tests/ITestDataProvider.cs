using System;
using System.Collections.Generic;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Tests;

public interface ITestDataProvider
{
    // Environment variable format (optional): 环境变量格式(可选)
    // CANKIT_TEST_ENDPOINT_PAIRS="virtual://alpha/0|virtual://alpha/1;socketcan://vcan0|socketcan://vcan1"
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; }

    // filters.Count == 0 => ignored
    public IEnumerable<(FilterMask[] filters, FilterFrame[] frames, int exceptResult)> MaskFilterCases { get; }

    public IEnumerable<(ICanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; }

    public IEnumerable<(ICanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; }

    public (int aBit, int dBit)? BaudRate { get; }

    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; }

    public record FilterRange(int Min, int Max, int Ide);
    public record FilterFrame(int Id, int Ide);
    public record FilterMask(int AccCode, int AccMask, int Ide);
}
