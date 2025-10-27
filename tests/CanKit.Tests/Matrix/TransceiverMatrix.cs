using System.Collections.Generic;
using System.Linq;

namespace CanKit.Tests.Matrix;

public partial class TestMatrix
{
    public static IEnumerable<object[]> CombinedOneShotClassic()
    {
        foreach(var i in Pairs())
        foreach (var r in ClassicFrameSettings())
            yield return i.Concat(r).ToArray();
    }

    public static IEnumerable<object[]> CombinedOneShotFD()
    {
        foreach(var i in Pairs())
        foreach (var r in FDFrameSettings())
            yield return i.Concat(r).ToArray();
    }

    public static IEnumerable<object[]> CombinedContinuosClassic()
    {
        foreach(var i in Pairs())
        foreach (var l in GapCases())
        foreach (var r in ClassicFrameSettings())
            yield return i.Concat(l).Concat(r).ToArray();
    }

    public static IEnumerable<object[]> CombinedContinuosFD()
    {
        foreach(var i in Pairs())
        foreach (var l in GapCases())
        foreach (var r in FDFrameSettings())
            yield return i.Concat(l).Concat(r).ToArray();
    }
}

public partial class TestMatrix
{
    private static IEnumerable<object[]> GapCases()
    {
        // (gapMs, lossLimit)
        yield return [1, 0.0]; // gap=1ms, loss < 0.1%
    }


    private static IEnumerable<object[]> FDFrameSettings()
    {
        // dataLen, BRS, IDE
        int[] len = [0, 64];
        foreach (var l in len)
        {
            yield return [l, false, false];
            yield return [l, true, false];
            yield return [l, false, true];
            yield return [l, true, true];
        }
    }

    private static IEnumerable<object[]> ClassicFrameSettings()
    {
        // dataLen, RTR, IDE
        int[] len = [0, 8];
        foreach (var l in len)
        {
            yield return [l, false, false];
            yield return [l, false, true];
        }

        yield return [0, true, false];
        yield return [0, false, false];
    }
}
