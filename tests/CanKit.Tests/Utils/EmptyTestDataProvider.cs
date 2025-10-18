using System;
using System.Collections.Generic;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;

namespace CanKit.Tests.Utils;

public class EmptyTestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } = [];
    public IEnumerable<(ITestDataProvider.FilterMask[] filters, ITestDataProvider.FilterFrame[] frames, int exceptResult)> MaskFilterCases { get; } = [];
    public IEnumerable<(ICanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; } = [];
    public IEnumerable<(ICanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; } = [];
    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; } = null;
}
