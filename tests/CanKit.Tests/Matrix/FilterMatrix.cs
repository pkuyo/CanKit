using System;
using System.Collections.Generic;
using System.Linq;

namespace CanKit.Tests.Matrix;

public partial class TestMatrix
{
    public static IEnumerable<object[]> CombinedRangeFilter()
    {
        foreach (var i in Pairs())
        foreach (var l in RangeCases())
            yield return i.Concat(l).ToArray();
    }

    public static IEnumerable<object[]> CombinedMaskFilter()
    {
        foreach (var i in Pairs())
        foreach (var l in MaskCases())
            yield return i.Concat(l).ToArray();
    }
}

public partial class TestMatrix
{
    private static IEnumerable<object[]> MaskCases()
    {
        foreach (var c in TestCaseProvider.Provider.MaskFilterCases)
            yield return [c.filters, c.frames, c.exceptResult];
        yield return
        [
            Array.Empty<ITestDataProvider.FilterMask>(), Array.Empty<ITestDataProvider.FilterFrame>(), 0
        ]; //Add empty case
    }

    private static IEnumerable<object[]> RangeCases()
    {
// 1) 标准帧区间 [0x100, 0x200]（含端点）
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x100, 0x200, 0) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x0FF, 0), // 255，低于下界 0x100 → 不匹配
                new(0x100, 0), // 命中下界 → 匹配
                new(0x150, 0), // 区间内 → 匹配
                new(0x200, 0), // 命中上界 → 匹配
                new(0x201, 0), // 高于上界 → 不匹配
                new(0x150, 1), // 扩展帧，Ide 不同 → 不匹配
                new(0x7FF, 0) // 高于上界 → 不匹配
            },
            3 // 期望匹配：0x100、0x150、0x200
        ];

// 2) 扩展帧区间 [0x1ABCDE00, 0x1ABCDEFF]
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x1ABCDE00, 0x1ABCDEFF, 1) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x1ABCDE00, 1), // 命中下界 → 匹配
                new(0x1ABCDE7F, 1), // 区间内 → 匹配
                new(0x1ABCDEFF, 1), // 命中上界 → 匹配
                new(0x1ABCDE12, 0), // 标准帧 → 不匹配（Ide 不同）
                new(0x1ABCDF00, 1), // 超出上界（0x1ABCDEFF < 0x1ABCDF00）→ 不匹配
                new(0x1ABCDD00, 1), // 低于下界（…CD**D**00 < …CD**E**00）→ 不匹配
                new(0x7FF, 0) // 标准帧且 ID 不在扩展区间 → 不匹配
            },
            3 // 期望匹配：下界、中间、上界三条
        ];

// 3) 标准帧：两段离散区间 [0x000,0x00F] ∪ [0x700,0x7FF]
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x000, 0x00F, 0), new(0x700, 0x7FF, 0) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x000, 0), // 命中第一段下界 → 匹配
                new(0x00F, 0), // 命中第一段上界 → 匹配
                new(0x010, 0), // 不在任一段内 → 不匹配
                new(0x6FF, 0), // 低于第二段下界 → 不匹配
                new(0x700, 0), // 命中第二段下界 → 匹配
                new(0x7AB, 0), // 位于第二段内 → 匹配
                new(0x7FF, 0), // 命中第二段上界 → 匹配
                new(0x7FF, 1) // 扩展帧 → 不匹配
            },
            5 // 期望匹配：0x000、0x00F、0x700、0x7AB、0x7FF（标准帧）
        ];

// 4) 混合：标准帧 [0x100,0x1FF]；扩展帧 [0x180,0x280]
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x100, 0x1FF, 0), new(0x180, 0x280, 1) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x17F, 0), // 标准帧，落在 [0x100,0x1FF] → 匹配
                new(0x180, 0), // 标准帧，区间内 → 匹配
                new(0x180, 1), // 扩展帧，命中扩展段下界 → 匹配
                new(0x200, 0), // 标准帧，高于 0x1FF → 不匹配
                new(0x200, 1), // 扩展帧，落在 [0x180,0x280] → 匹配
                new(0x281, 1), // 扩展帧，高于 0x280 → 不匹配
                new(0x100, 1) // 扩展帧，但扩展段下界为 0x180 → 不匹配
            },
            4 // 期望匹配：0x17F(0)、0x180(0)、0x180(1)、0x200(1)
        ];

// 5) 三段：标准 [0x050,0x060]、扩展 [0x1FFFF000,0x1FFFF010]、标准 [0x400,0x450]
        yield return
        [
            new ITestDataProvider.FilterRange[]
            {
                new(0x050, 0x060, 0),
                new(0x1FFFF000, 0x1FFFF010, 1),
                new(0x400, 0x450, 0)
            },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x04F, 0), // 标准，低于 0x050 → 不匹配
                new(0x050, 0), // 标准，命中段1下界 → 匹配
                new(0x055, 0), // 标准，段1内 → 匹配
                new(0x060, 0), // 标准，命中段1上界 → 匹配
                new(0x061, 0), // 标准，高于段1上界 → 不匹配
                new(0x1FFFF000, 1), // 扩展，命中段2下界 → 匹配
                new(0x1FFFF00F, 1), // 扩展，段2内 → 匹配
                new(0x1FFFF010, 1), // 扩展，命中段2上界 → 匹配
                new(0x1FFFF011, 1), // 扩展，高于段2上界 → 不匹配
                new(0x420, 0), // 标准，位于段3 [0x400,0x450] 内 → 匹配
                new(0x451, 0), // 标准，高于段3上界 → 不匹配
                new(0x420, 1) // 扩展，同值但 Ide=1，段3为标准 → 不匹配
            },
            7 // 期望匹配：段1三条(0x50/55/60) + 段2三条 + 段3一条(0x420)
        ];

// 6) 覆盖面广：标准全域 [0x000,0x7FF] + 扩展两个点与一个尾段 + 标准单点
        yield return
        [
            new ITestDataProvider.FilterRange[]
            {
                new(0x000, 0x7FF, 0), // 标准：全覆盖
                new(0x00000001, 0x00000001, 1), // 扩展：单点 1
                new(0x1FFFFFF0, 0x1FFFFFFF, 1), // 扩展：高尾段
                new(0x0123, 0x0123, 0) // 标准：单点 0x123（被全域也覆盖，用于重复匹配去重验证）
            },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x0123, 0), // 标准：命中（全域 & 单点）
                new(0x7FF, 0), // 标准：命中上界
                new(0x000, 0), // 标准：命中下界
                new(0x00000001, 1), // 扩展：命中单点
                new(0x00000001, 1), // 扩展：同上再来一条 → 也匹配
                new(0x1FFFFFFF, 1), // 扩展：命中尾段上界
                new(0x1FFFFFF1, 1), // 扩展：尾段内
                new(0x00000002, 1), // 扩展：不在任一扩展区间 → 不匹配
                new(0x800, 0), // 标准：高于 0x7FF → 不匹配
                new(0x7FE, 1) // 扩展：标准区与扩展区互不相干 → 不匹配
            },
            7 // 期望匹配：前三条标准 + 两条扩展(=1) + 尾段两条 = 7
        ];

// 7) 同一数值 ID，分别允许标准与扩展的单点
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x200, 0x200, 0), new(0x200, 0x200, 1) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x200, 0), // 标准：命中标准单点 → 匹配
                new(0x200, 1), // 扩展：命中扩展单点 → 匹配
                new(0x200, 0), // 标准：再次命中 → 匹配
                new(0x1FF, 0), // 标准：未命中单点 → 不匹配
                new(0x201, 1) // 扩展：未命中单点 → 不匹配
            },
            3 // 期望匹配：三条（两标准一扩展）
        ];

// 8) 标准帧两个重叠区间 [0x300,0x350] 与 [0x340,0x380]
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x300, 0x350, 0), new(0x340, 0x380, 0) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x33F, 0), // 落在第一段下界之前 → 不匹配
                new(0x340, 0), // 落在重叠起点（两段均含）→ 匹配
                new(0x345, 0), // 落在重叠区 → 匹配
                new(0x350, 0), // 第一段上界，同时在第二段内 → 匹配
                new(0x351, 0), // 只落在第二段内 → 匹配
                new(0x380, 0), // 第二段上界 → 匹配
                new(0x381, 0), // 高于所有区间 → 不匹配
                new(0x345, 1) // 扩展帧 → 不匹配
            },
            5 // 期望匹配：0x340、0x345、0x350、0x351、0x380
        ];

// 9) 扩展帧大区间 [0x18FF0000,0x18FFFFFF]
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x18FF0000, 0x18FFFFFF, 1) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x18FF0000, 1), // 下界 → 匹配
                new(0x18FFFFFF, 1), // 上界 → 匹配
                new(0x18FFAAAA, 1), // 区间内 → 匹配
                new(0x18FFAAAA, 0), // 标准帧 → 不匹配
                new(0x1FFFFFFF, 1), // 高于上界 → 不匹配
                new(0x18FEFFFF, 1) // 低于下界 → 不匹配
            },
            3 // 期望匹配：下界、上界、区间内各一条
        ];

// 10) 三段：标准 [0x010,0x01F]、扩展 [0x100,0x10F]、标准 [0x200,0x20F]
        yield return
        [
            new ITestDataProvider.FilterRange[] { new(0x010, 0x01F, 0), new(0x100, 0x10F, 1), new(0x200, 0x20F, 0) },
            new ITestDataProvider.FilterFrame[]
            {
                new(0x00F, 0), // 标准：低于 0x010 → 不匹配
                new(0x020, 0), // 标准：高于 0x01F → 不匹配
                new(0x100, 0), // 标准：不属于标准段（扩展段的下界）→ 不匹配
                new(0x10F, 0), // 标准：不属于标准段 → 不匹配
                new(0x201, 1), // 扩展：不在扩展段 [0x100,0x10F] → 不匹配
                new(0x123, 1), // 扩展：在 0x100–0x10F 之外 → 不匹配
                new(0x200, 1) // 扩展：落入标准段 [0x200,0x20F] 但 Ide=1 → 不匹配
            },
            0 // 期望匹配：无
        ];
    }
}
