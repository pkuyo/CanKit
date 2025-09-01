// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global

using Pkuyo.CanKit.Net.Core.Attributes;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Gen.Sample;

// If you don't see warnings, build the Analyzers Project.

[CanOption]
public partial class CanOptionTest
{
    [CanOptionItem("TestOption", CanOptionType.Init, "0.0f")]
     partial float test {  get;  set; }

    public partial void Apply(Pkuyo.CanKit.Net.Core.Abstractions.ICanApplier applier, bool force);
}

public class Examples
{
    public class MyCompanyClass // Try to apply quick fix using the IDE.
    {
    }

    public void ToStars()
    {
    }
}