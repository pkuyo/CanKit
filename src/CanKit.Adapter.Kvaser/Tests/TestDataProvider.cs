using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Tests;

namespace CanKit.Adapter.Kvaser.Tests;

public class TestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } =
        [("kvaser://0", "kvaser://1", true)];


    public IEnumerable<(ITestDataProvider.FilterMask[] filters,
        ITestDataProvider.FilterFrame[] frames,
        int exceptResult)> MaskFilterCases
    { get; } =
    [
        // Case 1: 不设置任何过滤 => 标准/扩展均全放行
                (
                    filters: [],
                    frames: [
                        new(0x007, 0), // std
                        new(0x123, 0), // std
                        new(0x18FF50E5, 1) // ext
                    ],
                    3
                ),

                // Case 2: 只设置标准过滤；扩展默认全放行
                // 标准过滤: 上 7 位等于 0x060（掩码 0x7F0）
                (
                    filters: [new ITestDataProvider.FilterMask(0x060, 0x7F0, 0)],
                    frames: [
                        new(0x060, 0), // 命中
                        new(0x061, 0), // 命中 (0x061 & 0x7F0 == 0x060)
                        new(0x160, 0), // 不命中
                        new(0x18FF50E5, 1), // 扩展未设，放行
                        new(0x1ABCDE01, 1) // 扩展未设，放行
                    ],
                    4
                ),

                // Case 3: 只设置扩展过滤；标准默认全放行
                // 扩展过滤: 0x18FFxxxx 范围 (code=0x18FF0000, mask=0x1FFF0000)
                (
                    filters: [new ITestDataProvider.FilterMask(0x18FF0000, 0x1FFF0000, 1)],
                    frames:
                    [
                        new(0x060, 0), // std 未设，放行
                        new(0x7FF, 0), // std 未设，放行
                        new(0x18FF50E5, 1), // 命中
                        new(0x1ABCDE01, 1) // 不命中
                    ],
                    exceptResult: 3
                ),

                // Case 4: 同时设置标准与扩展过滤（各自独立生效）
                (
                    filters:
                    [
                        new ITestDataProvider.FilterMask(0x100, 0x700, 0), // std
                        new ITestDataProvider.FilterMask(0x1ABC0000, 0x1FFF0000, 1) // ext
                    ],
                    frames:
                    [
                        new(0x100, 0), // std 命中
                        new(0x180, 0), // std 不命中
                        new(0x7FF, 0), // std 不命中
                        new(0x1ABC1234, 1), // ext 命中
                        new(0x18FF50E5, 1) // ext 不命中
                    ],
                    exceptResult: 2 + 1 // 实际为 3
                ),

                // Case 5: “覆盖”机制（标准）：后写覆盖前写
                // 先设 (0x060,0x7F0, std)，再设 (0x123,0x7FF, std) => 仅后者生效
                (
                    filters:
                    [
                        new ITestDataProvider.FilterMask(0x060, 0x7F0, 0),
                        new ITestDataProvider.FilterMask(0x123, 0x7FF, 0)
                    ],
                    frames:
                    [
                        new(0x060, 0), // 若按前者会过，但已被覆盖 => 不过
                        new(0x123, 0), // 命中后者 => 过
                        new(0x18FF50E5, 1) // 扩展未设，放行
                    ],
                    exceptResult: 2
                ),

                // Case 6: “覆盖”机制（扩展）：后写覆盖前写
                // 先设 0x18FFxxxx，后设 0x1ABCxxxx => 仅 0x1ABCxxxx 生效
                (
                    filters:
                    [
                        new ITestDataProvider.FilterMask(0x18FF0000, 0x1FFF0000, 1),
                        new ITestDataProvider.FilterMask(0x1ABC0000, 0x1FFF0000, 1)
                    ],
                    frames:
                    [
                        new(0x060, 0), // std 未设，放行
                        new(0x18FF50E5, 1), // 会被前者接收，但已被覆盖 => 不过
                        new(0x1ABC1234, 1) // 命中后者 => 过
                    ],
                    exceptResult: 2
                ),

                // Case 7: 扩展过滤“关闭”（mask=0），标准生效
                (
                    filters:
                    [
                        new ITestDataProvider.FilterMask(0x060, 0x7F0, 0), // std 有效
                        new ITestDataProvider.FilterMask(0x00000000, 0x00000000, 1) // ext 关闭 => 全放行
                    ],
                    frames:
                    [
                        new(0x060, 0), // std 命中
                        new(0x061, 0), // std 命中
                        new(0x160, 0), // std 不命中
                        new(0x18FF50E5, 1), // ext 放行
                        new(0x1ABCDE01, 1) // ext 放行
                    ],
                    exceptResult: 4
                ),

                // Case 8: 标准与扩展均显式“关闭”（mask=0）=> 全部放行
                (
                    filters:
                    [
                        new ITestDataProvider.FilterMask(0x00000000, 0x00000000, 0),
                        new ITestDataProvider.FilterMask(0x00000000, 0x00000000, 1)
                    ],
                    frames:
                    [
                        new(0x000, 0),
                        new(0x7FF, 0),
                        new(0x123, 0),
                        new(0x18FF50E5, 1),
                        new(0x00000001, 1)
                    ],
                    exceptResult: 5
                )
    ];


    public IEnumerable<(ICanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; } =
    [
        (new CanClassicFrame(0x501, new byte[] { 0x5, 0x1 }), TimeSpan.FromMilliseconds(5), 10),
        (new CanFdFrame(0x501, new byte[] { 0x5, 0x1 }, true), TimeSpan.FromMilliseconds(5), 10),
        (new CanClassicFrame(0x1ABCDEFF, new byte[] { 0x5, 0x1 }, true), TimeSpan.FromMilliseconds(5), 10),
        (new CanFdFrame(0x1ABCDEFF, new byte[] { 0x5, 0x1 }, true, false, true), TimeSpan.FromMilliseconds(5), 10)
    ];

    public IEnumerable<(ICanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; } = [];
    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; } = null;
}
