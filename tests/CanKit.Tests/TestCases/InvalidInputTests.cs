using System;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class InvalidInputTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void Classic_Data_Length_Over_8_Should_Throw()
    {
        Action act = () =>
        {
            var _ = new CanClassicFrame(0x123, new byte[9]);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fd_Data_Length_Over_64_Should_Throw()
    {
        Action act = () =>
        {
            var _ = new CanFdFrame(0x18DAF123, new byte[65], true, false, true);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Classic_Negative_Id_Should_Throw()
    {
        Action act = () =>
        {
            var _ = new CanClassicFrame(-1, ReadOnlyMemory<byte>.Empty, false);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

