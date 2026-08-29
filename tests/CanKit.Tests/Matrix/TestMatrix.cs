using System.Collections.Generic;
using Xunit;

namespace CanKit.Tests.Matrix;

public static partial class TestMatrix
{
    internal static IEnumerable<object> SkipWhenEmpty(IEnumerable<object[]> rows)
    {
        var any = false;
        foreach (var row in rows)
        {
            any = true;
            yield return row;
        }

        if (!any && TestCaseProvider.MissingAdapterSkipReason is { } reason)
            yield return new TheoryDataRow().WithSkip(reason);
    }

    public static IEnumerable<object> Pairs()
        => SkipWhenEmpty(PairRows());

    internal static IEnumerable<object[]> PairRows()
    {
        foreach (var endpoint in TestCaseProvider.Provider.EndpointPairs)
            yield return [endpoint.epA, endpoint.epB, $"{endpoint.epA}->{endpoint.epB}", endpoint.isFd];
    }
}
