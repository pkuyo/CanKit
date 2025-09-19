# Pkuyo.CanKit.Net.Gen — CAN 选项源码生成器

一个 Roslyn 增量源码生成器，用于减少 CAN 设备/通道选项类型的样板代码。只需为类型和属性加上特性，生成器会自动实现属性主体、变更跟踪以及统一的 `Apply` 方法。

包含项目：
- Pkuyo.CanKit.Net.Gen — 源码生成器（netstandard2.0）
- Pkuyo.CanKit.Net.Gen.Sample — 以 Analyzer 方式引用生成器的示例项目
- Pkuyo.CanKit.Net.Gen.Tests — 单元测试（可选）

## 工作原理
1) 在选项容器类型上标记 `CanOptionAttribute`。
2) 为每个选项声明“partial 自动属性”，并使用 `CanOptionItemAttribute(name, optionType, defaultValue?)` 标注。
3) 编译解决方案后，生成器会输出：私有 backing 字段、属性主体（get/set 或 get/init）、变更位图，以及 `Apply(ICanApplier applier, bool force)` 方法。

`Apply` 仅在对应位发生变化（或 `force == true`）且 `CanOptionType` 与应用阶段匹配时才推送下发。

## 最小示例
```csharp
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;

[CanOption]
public partial class MyChannelOptions
{
    [CanOptionItem("baud", CanOptionType.Init, "500_000")]
    public partial uint BaudRate { get; set; }

    [CanOptionItem("workMode", CanOptionType.Init)]
    public partial ChannelWorkMode WorkMode { get; set; }
}

// 生成内容：
// - backing 字段与 get/set 主体
// - 变更位图（BitArray）
// - public partial void Apply(ICanApplier applier, bool force)
```

## 在项目中启用生成器
请以 Analyzer 的方式引用生成器项目（而非普通引用）：
```xml
<ProjectReference Include="..\Pkuyo.CanKit.Net.Gen\Pkuyo.CanKit.Net.Gen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```
示例参考：`Pkuyo.CanKit.Net.Gen.Sample/Pkuyo.CanKit.Net.Gen.Sample.csproj`。

## 诊断（Diagnostics）
- CANG001（错误）：属性必须是带 partial 的自动属性，且包含 get/set 或 get/init。
- CANG002（错误）：存在访问修饰符但无法确定访问级别。

发布跟踪文件见：`AnalyzerReleases.Shipped.md` 与 `AnalyzerReleases.Unshipped.md`。

## 使用建议
- 仅声明 partial 自动属性，属性主体由生成器输出。
- 可在特性上提供默认值。
- `Apply` 的 `force == true` 可在无需变更时强制重新下发。

## 延伸阅读
- Roslyn 源码生成器入门：https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md

英文文档见：`Readme.md`。

