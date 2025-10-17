using System.Collections.Generic;
using System.Linq;

namespace CanKit.Tests.Matrix;

public static partial class TestMatrix
{

    public static IEnumerable<object[]> Pairs()
    {
        foreach (var endpoint in TestCaseProvider.Provider.EndpointPairs)
            yield return [endpoint.epA, endpoint.epB,$"{endpoint.epA}->{endpoint.epB}", endpoint.isFd];
    }
}


