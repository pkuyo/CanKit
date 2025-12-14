using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

public class CanFrameTests : IClassFixture<TestCaseProvider>
{
    [Fact]
    public void Classic_Data_Length_Over_8_Should_Throw()
    {
        Action act = () =>
        {
            var _ = CanFrame.Classic(0x123, new byte[9]);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fd_Data_Length_Over_64_Should_Throw()
    {
        Action act = () =>
        {
            var _ = CanFrame.Fd(0x18DAF123, new byte[65], true, false, true);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Classic_Negative_Id_Should_Throw()
    {
        Action act = () =>
        {
            var _ = CanFrame.Classic(-1, ReadOnlyMemory<byte>.Empty, false);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }


    [Fact]
    public void Classic_RTR_HasData_Should_Throw()
    {
        Action act = () =>
        {
            var _ = CanFrame.Classic(-1, new ReadOnlyMemory<byte>([0x11, 0x22, 0x33]), isRemoteFrame: true);
        };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [MemberData(nameof(Matrix.TestMatrix.ClassicFrameSettings), MemberType = typeof(Matrix.TestMatrix))]
    public void Classic_Frame_Build(int len, bool ext, bool rtr)
    {
        len = rtr ? 0 : len;
        var data = new byte[len];
        TestCaseProvider.Rand.NextBytes(data);
        var frameId = TestCaseProvider.Rand.Next(ext ? 0x1FFFFFFF : 0x7FF);

        var frame = CanFrame.Classic(frameId,new ReadOnlyMemory<byte>(data), ext, rtr);
        frame.FrameKind.Should().Be(CanFrameType.Can20);
        frame.ID.Should().Be(frameId);
        frame.IsExtendedFrame.Should().Be(ext);
        frame.IsRemoteFrame.Should().Be(rtr);
        frame.Data.Span.SequenceEqual(data.AsSpan());
        frame.Dlc.Should().Be((byte)len);
    }

    [Theory]
    [MemberData(nameof(Matrix.TestMatrix.FDFrameSettings), MemberType = typeof(Matrix.TestMatrix))]
    public void Fd_Frame_Build(int len, bool ext, bool brs)
    {
        var data = new byte[len];
        TestCaseProvider.Rand.NextBytes(data);
        var frameId = TestCaseProvider.Rand.Next(ext ? 0x1FFFFFFF : 0x7FF);

        var frame = CanFrame.Fd(frameId, new ReadOnlyMemory<byte>(data), brs, isExtendedFrame: ext);
        frame.FrameKind.Should().Be(CanFrameType.CanFd);
        frame.ID.Should().Be(frameId);
        frame.IsExtendedFrame.Should().Be(ext);
        frame.BitRateSwitch.Should().Be(brs);
        frame.Data.Span.SequenceEqual(data.AsSpan());
        CanFrame.DlcToLen(frame.Dlc).Should().Be((byte)len);
    }
}

