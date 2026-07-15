using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Definitions;
using CanKit.Pro.RawCan;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// Verifies <see cref="CanIdFilter.Overlaps"/> and
/// <see cref="ICanBusService.FindOverlappingFilterSubscriptions"/> (arc42 "Adressierungs-Helfer",
/// SRS FR-RAW-041).
/// </summary>
public class CanIdFilterOverlapTests : IClassFixture<TestCaseProvider>
{
    private static string NewSession() => $"overlap-{Guid.NewGuid():N}";

    private static ICanBus Open(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    [Fact]
    public void Range_Filters_That_Overlap_Are_Detected()
    {
        var a = CanIdFilter.Range(0x100, 0x1FF);
        var b = CanIdFilter.Range(0x180, 0x2FF);

        a.Overlaps(b).Should().BeTrue();
        b.Overlaps(a).Should().BeTrue("overlap must be symmetric");
    }

    [Fact]
    public void Range_Filters_That_Are_Disjoint_Are_Not_Detected_As_Overlapping()
    {
        var a = CanIdFilter.Range(0x100, 0x1FF);
        var b = CanIdFilter.Range(0x200, 0x2FF);

        a.Overlaps(b).Should().BeFalse();
    }

    [Fact]
    public void Range_Filters_On_Different_Id_Types_Never_Overlap_Even_With_Numerically_Identical_Bounds()
    {
        var std = CanIdFilter.Range(0x100, 0x1FF, CanFilterIDType.Standard);
        var ext = CanIdFilter.Range(0x100, 0x1FF, CanFilterIDType.Extend);

        std.Overlaps(ext).Should().BeFalse();
    }

    [Fact]
    public void Mask_Filters_That_Overlap_Are_Detected()
    {
        // Both accept any ID with bit 0x100 set; they disagree only on bits neither one masks.
        var a = CanIdFilter.Mask(accCode: 0x100, accMask: 0x100);
        var b = CanIdFilter.Mask(accCode: 0x100, accMask: 0x300);

        a.Overlaps(b).Should().BeTrue();
    }

    [Fact]
    public void Mask_Filters_That_Disagree_On_A_Commonly_Masked_Bit_Do_Not_Overlap()
    {
        var a = CanIdFilter.Mask(accCode: 0x000, accMask: 0x100); // bit 0x100 must be 0
        var b = CanIdFilter.Mask(accCode: 0x100, accMask: 0x100); // bit 0x100 must be 1

        a.Overlaps(b).Should().BeFalse();
    }

    [Fact]
    public void Range_And_Mask_Filters_That_Overlap_Are_Detected()
    {
        var range = CanIdFilter.Range(0x100, 0x10F);
        // Mask matches 0x1F0..0x1FF (bits 0x1F0 fixed, low 4 bits free) -- no overlap with the range.
        var nonOverlappingMask = CanIdFilter.Mask(accCode: 0x1F0, accMask: 0x7F0);
        // Mask matches 0x100..0x10F (bits 0x1F0 fixed to 0x100, low 4 bits free) -- overlaps the range.
        var overlappingMask = CanIdFilter.Mask(accCode: 0x100, accMask: 0x7F0);

        range.Overlaps(nonOverlappingMask).Should().BeFalse();
        range.Overlaps(overlappingMask).Should().BeTrue();
        overlappingMask.Overlaps(range).Should().BeTrue("overlap must be symmetric regardless of argument order");
    }

    [Fact]
    public void Range_And_Mask_Filters_Honor_Acceptance_Mask_Bits_Above_The_29Bit_Id_Space()
    {
        // No real CAN ID ever has bit 29 set (IDs are at most 29 bits wide), so a mask that
        // requires bit 29 to be 1 can never actually be satisfied by any ID -- including every ID
        // in 'range'. The range/mask overlap check must honor that acceptance-mask bit even though
        // it falls outside the bits a valid range bound can vary over.
        var range = CanIdFilter.Range(0x100, 0x10F, CanFilterIDType.Extend);
        var unsatisfiableMask = CanIdFilter.Mask(accCode: 0x20000100, accMask: 0x20000700, idType: CanFilterIDType.Extend);

        range.Overlaps(unsatisfiableMask).Should().BeFalse();
        unsatisfiableMask.Overlaps(range).Should().BeFalse("overlap must be symmetric regardless of argument order");
    }

    [Fact]
    public void Range_Filters_Whose_Numeric_Overlap_Lies_Entirely_Above_The_Standard_11Bit_Space_Do_Not_Overlap()
    {
        // [0x7F0, 0x900] and [0x800, 0x810] intersect numerically, but Matches() only ever sees
        // 11-bit standard IDs (<= 0x7FF), so no standard frame can ever match the second filter.
        var a = CanIdFilter.Range(0x7F0, 0x900);
        var b = CanIdFilter.Range(0x800, 0x810);

        a.Overlaps(b).Should().BeFalse();
        b.Overlaps(a).Should().BeFalse("overlap must be symmetric regardless of argument order");
    }

    [Fact]
    public void Range_Filters_Whose_Numeric_Overlap_Lies_Entirely_Above_The_Extended_29Bit_Space_Do_Not_Overlap()
    {
        var a = CanIdFilter.Range(0x1FFFFFF0, 0x20000100, CanFilterIDType.Extend);
        var b = CanIdFilter.Range(0x20000000, 0x20000010, CanFilterIDType.Extend);

        a.Overlaps(b).Should().BeFalse();
        b.Overlaps(a).Should().BeFalse("overlap must be symmetric regardless of argument order");
    }

    [Fact]
    public void FindOverlappingFilterSubscriptions_Reports_Overlapping_Registered_Subscriptions()
    {
        using var bus = Open(NewSession(), 0);
        using var service = new CanBusService(bus);

        using var a = service.Subscribe(CanIdFilter.Range(0x100, 0x1FF));
        using var b = service.Subscribe(CanIdFilter.Range(0x180, 0x2FF));
        using var c = service.Subscribe(CanIdFilter.Range(0x300, 0x3FF)); // disjoint from both

        var overlaps = service.FindOverlappingFilterSubscriptions();

        overlaps.Should().ContainSingle();
        var pair = overlaps[0];
        new[] { pair.First, pair.Second }.Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void FindOverlappingFilterSubscriptions_Ignores_Predicate_Based_Subscriptions()
    {
        using var bus = Open(NewSession(), 0);
        using var service = new CanBusService(bus);

        using var predicateSub = service.Subscribe(view => view.ID == 0x150); // would numerically overlap 'range' but is opaque
        using var range = service.Subscribe(CanIdFilter.Range(0x100, 0x1FF));

        service.FindOverlappingFilterSubscriptions().Should().BeEmpty();
    }

    [Fact]
    public void FindOverlappingFilterSubscriptions_Returns_Empty_When_Nothing_Overlaps()
    {
        using var bus = Open(NewSession(), 0);
        using var service = new CanBusService(bus);

        using var a = service.Subscribe(CanIdFilter.Range(0x100, 0x1FF));
        using var b = service.Subscribe(CanIdFilter.Range(0x200, 0x2FF));

        service.FindOverlappingFilterSubscriptions().Should().BeEmpty();
    }
}
