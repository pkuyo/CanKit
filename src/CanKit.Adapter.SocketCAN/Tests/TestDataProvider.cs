using CanKit.Core.Definitions;
using CanKit.Tests;

namespace CanKit.Adapter.SocketCAN.Tests;

public class TestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } =
        [("socketcan://vcan0", "socketcan://vcan1", true)];

    public IEnumerable<(ITestDataProvider.FilterMask[] filters, ITestDataProvider.FilterFrame[] frames, int exceptResult)> MaskFilterCases
    { get; } =
    [
        // Case 1: 标准帧 — 精确匹配（11 位 ID）
        // accMask = 0x7FF 覆盖全部 11 位，只有 0x123 且为标准帧能通过
        (
            filters:
            [
                new(AccCode: 0x123, AccMask: 0x7FF, Ide: 0)
            ],
            frames:
            [
                new(Id: 0x123, Ide: 0), // 匹配
                new(Id: 0x124, Ide: 0), // 不匹配（ID 不同）
                new(Id: 0x123, Ide: 1) // 不匹配（IDE 不同）
            ],
            exceptResult: 1
        ),

        // Case 2: 标准帧 — 前缀范围匹配
        // 约束高 3 位：accCode = 0x100, accMask = 0x700
        // 匹配范围大致是 0x100 ~ 0x1FF（满足 (id & 0x700) == 0x100）
        (
            filters:
            [
                new(AccCode: 0x100, AccMask: 0x700, Ide: 0)
            ],
            frames:
            [
                new(Id: 0x100, Ide: 0), // 匹配（同前缀）
                new(Id: 0x1AB & 0x7FF, Ide: 0), // 例如 0x1AB 也匹配
                new(Id: 0x200, Ide: 0), // 不匹配（高位前缀不同）
                new(Id: 0x150, Ide: 1) // 不匹配（IDE 不同）
            ],
            exceptResult: 2
        ),

        // Case 3: 扩展帧 — 精确匹配（29 位 ID）
        // 用满 29 位掩码：0x1FFFFFFF
        (
            filters:
            [
                new(AccCode: 0x01ABCDE, AccMask: 0x1FFFFFFF, Ide: 1)
            ],
            frames:
            [
                new(Id: 0x01ABCDE, Ide: 1), // 匹配
                new(Id: 0x01ABCDD, Ide: 1), // 不匹配（ID 不同）
                new(Id: 0x01ABCDE, Ide: 0) // 不匹配（IDE 不同）
            ],
            exceptResult: 1
        ),

        // Case 4: 扩展帧 — 范围匹配（高位分组）
        // 例如匹配 0x01ABCxxx 这类块：accCode = 0x01ABC000, accMask = 0x1FFF000
        // 条件：(id & 0x1FFF000) == 0x01ABC000
        (
            filters:
            [
                new(AccCode: 0x01ABC000, AccMask: 0x1FFF000, Ide: 1)
            ],
            frames:
            [
                new(Id: 0x01ABC123, Ide: 1), // 匹配
                new(Id: 0x01ABCF00, Ide: 1), // 匹配
                new(Id: 0x01ABD123, Ide: 1), // 不匹配（高位不符）
                new(Id: 0x01ABC123, Ide: 0) // 不匹配（IDE 不同）
            ],
            exceptResult: 2
        ),

        // Case 5: 多过滤器联合（标准帧）— 任一命中即通过
        (
            filters:
            [
                new(AccCode: 0x055, AccMask: 0x7FF, Ide: 0),
                new(AccCode: 0x5AA, AccMask: 0x7FF, Ide: 0)
            ],
            frames:
            [
                new(Id: 0x055, Ide: 0), // 命中过滤器1
                new(Id: 0x5AA, Ide: 0), // 命中过滤器2
                new(Id: 0x123, Ide: 0), // 未命中
                new(Id: 0x5AA, Ide: 1) // IDE 不同，不通过
            ],
            exceptResult: 2
        ),

        // Case 6: 全接收（Catch-all）
        // 按 socketcan 规则，accMask=0 表示 (id & 0) == (accCode & 0) 恒成立，因此接收所有同 IDE 的帧。
        // 这里给两条：一条标准一条扩展，分别用两个过滤器来全接收。
        (
            filters:
            [
                new(AccCode: 0x00000000, AccMask: 0x00000000, Ide: 0), // 接收所有标准帧
                new(AccCode: 0x00000000, AccMask: 0x00000000, Ide: 1) // 接收所有扩展帧
            ],
            frames:
            [
                new(Id: 0x000, Ide: 0),
                new(Id: 0x7FF, Ide: 0),
                new(Id: 0x01ABCDE, Ide: 1),
                new(Id: 0x1FFFFFFF, Ide: 1)
            ],
            exceptResult: 4
        ),

        // Case 7: 同一数值 ID，同时接收标准与扩展需分别建过滤器
        // 在 socketcan 中，IDE（EFF）位是参与匹配的，因此要同时接收 std/eff 的同一 ID，需要两个过滤器。
        (
            filters:
            [
                new(AccCode: 0x7DF, AccMask: 0x7FF, Ide: 0), // 标准 0x7DF
                new(AccCode: 0x000007DF, AccMask: 0x1FFFFFFF, Ide: 1) // 扩展 0x7DF
            ],
            frames:
            [
                new(Id: 0x7DF, Ide: 0), // 命中过滤器1
                new(Id: 0x7DF, Ide: 1), // 命中过滤器2
                new(Id: 0x7E0, Ide: 0) // 未命中
            ],
            exceptResult: 2
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
}
