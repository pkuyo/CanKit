using System;
using System.Collections.Generic;
using System.Linq;

namespace CanKit.Tests;

public static partial class TestMatrix
{
    // Environment variable format (optional): 环境变量格式(可选)
    // CANKIT_TEST_ENDPOINT_PAIRS="virtual://alpha/0|virtual://alpha/1;socketcan://vcan0|socketcan://vcan1"
    public static IEnumerable<object[]> Pairs()
    {
        var env = Environment.GetEnvironmentVariable("CANKIT_TEST_ENDPOINT_PAIRS");
        bool hasValue = false;
        if (!string.IsNullOrWhiteSpace(env))
        {
            foreach (var part in env.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
#if NET5_0_OR_GREATER
                var pair = part.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
#else
                var pair = part.Split(['|'], StringSplitOptions.RemoveEmptyEntries);
#endif
                if (pair.Length == 3 && bool.TryParse(pair[2], out var hasFd))
                {
                    yield return [pair[0], pair[1], $"env:{pair[0]}->{pair[1]}", hasFd];
                    hasValue = true;
                }
            }
            if(hasValue)
                yield break;
        }

        // Default to Virtual endpoints (always available) 默认endpoint时virtual
        yield return ["virtual://alpha/0  ", "virtual://alpha/1", "virtual-alpha", false];
    }

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

public static partial class TestMatrix
{
    private static IEnumerable<object[]> GapCases()
    {
        // (gapMs, lossLimit)
        yield return [1, 0.001]; // gap=1ms, loss < 0.1%
        yield return [0, 0.05]; // gap=0ms, loss < 5%
    }

    private static IEnumerable<object[]> FDFrameSettings()
    {
        // dataLen, BRS, IDE
        int[] len = [0, 8, 48, 64];
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
        int[] len = [0, 4, 8];
        foreach (var l in len)
        {
            yield return [l, false, false];
            yield return [l, false, true];
        }

        yield return [0, true, false];
        yield return [0, false, false];
    }
}

