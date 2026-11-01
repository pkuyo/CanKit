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

    private sealed class CountingOwner(byte[] buffer) : System.Buffers.IMemoryOwner<byte>
    {
        public int Disposed;
        public Memory<byte> Memory { get; } = buffer;
        public void Dispose() => Disposed++;
    }

    [Fact]
    public void Dispose_Without_OwnMemory_Does_Not_Release_The_Owner()
    {
        var owner = new CountingOwner(new byte[4]);
        var frame = CanFrame.Classic(0x1, owner, ownMemory: false);

        frame.Dispose();

        owner.Disposed.Should().Be(0);
    }

    [Fact]
    public void Dispose_With_OwnMemory_Releases_The_Owner()
    {
        var owner = new CountingOwner(new byte[4]);
        var frame = CanFrame.Classic(0x1, owner);

        frame.Dispose();

        owner.Disposed.Should().Be(1);
    }

    [Fact]
    public void Copy_Is_Independent_Of_The_Source_Memory()
    {
        var owner = new CountingOwner(new byte[] { 1, 2, 3 });
        var source = CanFrame.Fd(0x1ABCDE, owner, BRS: true, ESI: true, isExtendedFrame: true);

        var clone = source.Copy(new DefaultBufferAllocator());
        source.Dispose();
        owner.Memory.Span.Clear();

        clone.Data.ToArray().Should().Equal(1, 2, 3);
        clone.ID.Should().Be(source.ID);
        clone.Flags.Should().Be(source.Flags);
        clone.FrameKind.Should().Be(CanFrameType.CanFd);
        owner.Disposed.Should().Be(1);
    }

    [Fact]
    public void Copy_Owns_Its_Buffer()
    {
        var rented = new CountingOwner(new byte[2]);
        var allocator = new SingleOwnerAllocator(rented);

        var clone = CanFrame.Classic(0x1, new byte[] { 5, 6 }).Copy(allocator);
        clone.Dispose();

        rented.Disposed.Should().Be(1);
    }

    private sealed class SingleOwnerAllocator(System.Buffers.IMemoryOwner<byte> owner) : CanKit.Abstractions.SPI.Common.IBufferAllocator
    {
        public System.Buffers.IMemoryOwner<byte> Rent(int length, bool zeroFill = false) => owner;
        public bool FrameNeedDispose => true;
    }
}

