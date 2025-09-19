using System;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.Tests;

public class DeviceTypeTests
{
    [Fact]
    public void Register_Duplicate_Id_Throws()
    {
        var id = $"dup-{Guid.NewGuid():N}";
        var first = DeviceType.Register(id, 1);
        Assert.NotNull(first);
        Assert.Throws<InvalidOperationException>(() => DeviceType.Register(id, 2));
    }

    [Fact]
    public void TryFromId_Is_CaseInsensitive()
    {
        var id = $"case-{Guid.NewGuid():N}";
        var dt = DeviceType.Register(id, 123);
        Assert.True(DeviceType.TryFromId(id.ToUpperInvariant(), out var upper));
        Assert.True(dt.Equals(upper));
    }

    [Fact]
    public void FromId_Unknown_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => DeviceType.FromId($"unknown-{Guid.NewGuid():N}"));
    }
}

