// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Gen.Sample;


[CanOption]
public partial class CanOptionSample : ICanOptions
{
    [CanOptionItem("TestOption", CanOptionType.Init, "0.0f")]
     partial float Test {  get;  set; }
     
    [CanOptionItem("TestClassOption", CanOptionType.Init, "new object()")]
    partial object TestObject { get; set; }

    public ICanModelProvider Provider { get; }
    public partial void Apply(Pkuyo.CanKit.Net.Core.Abstractions.ICanApplier applier, bool force);
}

