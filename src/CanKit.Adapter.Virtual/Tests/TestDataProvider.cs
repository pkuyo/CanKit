using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using CanKit.Tests;

namespace CanKit.Adapter.Virtual.Tests;

public class TestDataProvider : ITestDataProvider
{
    public IEnumerable<(string epA, string epB, bool isFd)> EndpointPairs { get; } =
        [("virtual://alpha/0", "virtual://alpha/1", true)];

    public IEnumerable<(ITestDataProvider.FilterMask[] filters, ITestDataProvider.FilterFrame[] frames, int exceptResult)> MaskFilterCases
    { get; } =
    [
        // Case 1: ide=0 标准帧 — 精确匹配（等价于只看低11位）
        (
            filters:
            [
                new(AccCode: 0x123, AccMask: 0x7FF, Ide: 0)
            ],
            frames:
            [
                new(Id: 0x123, Ide: 0), // 匹配（低11位相等，且为标准帧）
                new(Id: 0x124, Ide: 0), // 不匹配（低11位不同）
                new(Id: 0x123, Ide: 1) // 不匹配（扩展帧）
            ],
            exceptResult: 1
        ),

        // Case 2: ide=0 标准帧 — 只用低11位；高位mask与code应被忽略
        // 这里故意把 accCode 和 accMask 的高位设成1，但只看低11位 -> 实际等价于 accMask=0x7FF, accCode低11位=0x2A3
        (
            filters:
            [
                new(AccCode: 0x00F02A3, AccMask: unchecked((int)0xFFFF_FFFF), Ide: 1)
            ],
            frames:
            [
                new(Id: 0x2A3, Ide: 0), // 标准帧不匹配
                new(Id: 0x00F02A3, Ide: 0), // 标准帧不匹配
                new(Id: 0x00F02A3, Ide: 1), // 匹配
                new(Id: 0x2A3, Ide: 1) // 不匹配（扩展帧）
            ],
            exceptResult: 1
        ),

        // Case 3: ide=0 标准帧 — 前缀范围匹配（只在低11位上起作用）
        // 低11位掩码0x700，限定高3位为 0b001 -> (id & 0x700) == 0x100
        (
            filters:
            [
                new(AccCode: 0x100, AccMask: 0x700, Ide: 0)
            ],
            frames:
            [
                new(Id: 0x1AB, Ide: 0), // 匹配（低11位前缀=0x100）
                new(Id: 0x150, Ide: 0), // 匹配
                new(Id: 0x200, Ide: 0), // 不匹配
                new(Id: 0x150, Ide: 1) // 不匹配（扩展帧即使低11位满足也不行）
            ],
            exceptResult: 2
        ),

        // Case 4: ide=1 扩展帧 — 精确匹配（29位）
        (
            filters:
            [
                new(AccCode: 0x01ABCDE, AccMask: 0x1FFFFFFF, Ide: 1)
            ],
            frames:
            [
                new(Id: 0x01ABCDE, Ide: 1), // 匹配（29位全等）
                new(Id: 0x01ABCDD, Ide: 1), // 不匹配（仅低11位可能相近，但高位不同→不匹配）
                new(Id: 0x01ABCDE, Ide: 0) // 不匹配（标准帧）
            ],
            exceptResult: 1
        ),

        // Case 5: ide=1 扩展帧 — 范围匹配（部分高位前缀）
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
                new(Id: 0x01ABD123, Ide: 1), // 不匹配（前缀不同）
                new(Id: 0x01ABC123, Ide: 0) // 不匹配（标准帧）
            ],
            exceptResult: 2
        ),

        // Case 6: 混合过滤器（ide区分）— 标准用低11位、扩展用29位
        (
            filters:
            [
                new(AccCode: 0x7DF, AccMask: 0x7FF, Ide: 0), // 标准帧 OBD 0x7DF
                new(AccCode: 0x18DAF110, AccMask: 0x1FFFFFFF, Ide: 1) // 扩展帧某诊断ID
            ],
            frames:
            [
                new(Id: 0x7DF, Ide: 0), // 命中过滤器1（低11位）
                new(Id: 0x18DAF110, Ide: 1), // 命中过滤器2（29位）
                new(Id: 0x7DF, Ide: 1), // 不匹配（扩展帧）
                new(Id: 0x18DAF110, Ide: 0), // 不匹配（标准帧）
                new(Id: 0x7E0, Ide: 0) // 不匹配（低11位不同）
            ],
            exceptResult: 2
        ),

        // Case 7: “全接收”按ide分开
        // accMask=0 在该语义下：
        //  - ide=0：接受所有标准帧（仅低11位参与，但全0掩码恒成立）
        //  - ide=1：接受所有扩展帧（29位掩码全0恒成立）
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
        )
    ];

    public IEnumerable<(ICanFrame frame, TimeSpan period, int count)> PeriodicCountCases { get; } =
    [
        (new CanClassicFrame(0x501, new byte[] { 0x5, 0x1 }), TimeSpan.FromMilliseconds(5), 10),
        (new CanFdFrame(0x501, new byte[] { 0x5, 0x1 }, true), TimeSpan.FromMilliseconds(5), 10),
        (new CanClassicFrame(0x1ABCDEFF, new byte[] { 0x5, 0x1 }, true), TimeSpan.FromMilliseconds(5), 10),
        (new CanFdFrame(0x1ABCDEFF, new byte[] { 0x5, 0x1 }, true, true), TimeSpan.FromMilliseconds(5), 10)
    ];

    public IEnumerable<(ICanFrame frame, TimeSpan period, float deviation)> PeriodicPeriodCases { get; } = [];
    public (int aBit, int dBit)? BaudRate { get; } = null;

    public Action<IBusInitOptionsConfigurator>? TestBusInitFunc { get; } = null;
}
