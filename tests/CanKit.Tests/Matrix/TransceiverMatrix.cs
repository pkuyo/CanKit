using System.Collections.Generic;
using System.Linq;

namespace CanKit.Tests.Matrix;

public partial class TestMatrix
{
    public static IEnumerable<object> CombinedOneShotClassic()
        => SkipWhenEmpty(CombinedOneShotClassicRows());

    private static IEnumerable<object[]> CombinedOneShotClassicRows()
    {
        foreach (var i in PairRows())
        foreach (var r in ClassicFrameSettings())
            yield return i.Concat(r).ToArray();
    }

    public static IEnumerable<object> CombinedOneShotFD()
        => SkipWhenEmpty(CombinedOneShotFDRows());

    private static IEnumerable<object[]> CombinedOneShotFDRows()
    {
        foreach (var i in PairRows())
        foreach (var r in FDFrameSettings())
            yield return i.Concat(r).ToArray();
    }

    public static IEnumerable<object> CombinedContinuosClassic()
        => SkipWhenEmpty(CombinedContinuosClassicRows());

    private static IEnumerable<object[]> CombinedContinuosClassicRows()
    {
        foreach (var i in PairRows())
        foreach (var l in GapCases())
        foreach (var r in ClassicFrameSettings())
            yield return i.Concat(l).Concat(r).ToArray();
    }

    public static IEnumerable<object> CombinedContinuosFD()
        => SkipWhenEmpty(CombinedContinuosFDRows());

    private static IEnumerable<object[]> CombinedContinuosFDRows()
    {
        foreach (var i in PairRows())
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


    public static IEnumerable<object[]> FDFrameSettings()
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

    public static IEnumerable<object[]> ClassicFrameSettings()
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
