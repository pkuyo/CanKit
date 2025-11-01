using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Tests.Matrix;

public partial class TestMatrix
{
    public static IEnumerable<object[]> CombinedPeriodicCount()
    {
        foreach(var i in Pairs())
        foreach (var l in PeriodicCountCases())
            yield return i.Concat(l).ToArray();
    }

    public static IEnumerable<object[]> CombinedPeriodicPeriod()
    {
        foreach(var i in Pairs())
        foreach (var l in PeriodicPeriodCases())
            yield return i.Concat(l).ToArray();
    }
}

public partial class TestMatrix
{
    private static IEnumerable<object[]> PeriodicCountCases()
    {
        bool hasData = false;
        foreach (var i in TestCaseProvider.Provider.PeriodicCountCases)
        {
            yield return [i.frame, i.period, i.count];
            hasData = true;
        }

        if (!hasData)
        {
            yield return [new CanFrame(), TimeSpan.Zero, 0];
        }
    }

    private static IEnumerable<object[]> PeriodicPeriodCases()
    {
        bool hasData = false;
        foreach (var i in TestCaseProvider.Provider.PeriodicPeriodCases)
        {
            yield return [i.frame, i.period, i.deviation];
            hasData = true;
        }

        if (!hasData)
        {
            yield return [new CanFrame(), TimeSpan.Zero, 0];
        }
    }
}
