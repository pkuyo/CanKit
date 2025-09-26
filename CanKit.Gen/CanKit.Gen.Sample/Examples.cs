// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

using System;
using CanKit.Core.Abstractions;
using CanKit.Core.Attributes;
using CanKit.Core.Definitions;

namespace CanKit.Gen.Sample;


[CanOption]
public partial class CanOptionSample : ICanOptions
{
    [CanOptionItem("TestOption", CanOptionType.Init, "0.0f")]
    partial float Test { get; set; }

    [CanOptionItem("TestClassOption", CanOptionType.Init, "new object()")]
    partial object TestObject { get; set; }

    public ICanModelProvider Provider => throw new NotImplementedException();
    public partial void Apply(ICanApplier applier, bool force);
}

