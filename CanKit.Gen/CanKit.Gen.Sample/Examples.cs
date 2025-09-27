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
    public float Test
    {
        get => Get_Test();
        set => Set_Test(value);
    }

    [CanOptionItem("TestClassOption", CanOptionType.Init, "new object()")]
    public object TestObject
    {
        get => Get_TestObject();
        set => Set_TestObject(value);
    }

    public ICanModelProvider Provider => throw new NotImplementedException();
    public partial void Apply(ICanApplier applier, bool force);
}

