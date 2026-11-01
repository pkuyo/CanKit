using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;

namespace CanKit.Adapter.ControlCAN;

internal enum ControlCanFilterMode
{
    None,
    HardwareMask,
    HardwareRanges,
    Software
}

internal sealed class ControlCanFilterPlan(IFilterRule[] rules, ControlCanFilterMode mode,
    Func<CanFrame, bool>? softwarePredicate, uint accCode = 0, uint accMask = uint.MaxValue)
{
    public IFilterRule[] Rules { get; } = rules;
    public ControlCanFilterMode Mode { get; } = mode;
    public Func<CanFrame, bool>? SoftwarePredicate { get; } = softwarePredicate;
    public uint AccCode { get; } = accCode;
    public uint AccMask { get; } = accMask;
}

/// <summary>Builds and validates a private filter snapshot without changing native channel state.</summary>
internal static class ControlCanFilterPlanner
{
    public static ControlCanFilterPlan Prepare(IBusOptions options, bool supportsRangeTable)
    {
        var rules = options.Filter.FilterRules.ToArray();
        // Explicit software rules form an additional filter stage, whether the configured
        // rules run in hardware or in software. Never append derived rules to caller options.
        var explicitSoftwareFilter = options.Filter.SoftwareFilterRules.Count == 0
            ? null : FilterRule.Build(options.Filter.SoftwareFilterRules);
        var hardwareMask = GetHardwareMask(rules, options.Features);
        if (hardwareMask != null)
        {
            var accMask = hardwareMask.AccMask | SjaUnusedBits(hardwareMask.FilterIdType);
            // SJA1000 compares register bits without an IDE discriminator. Another frame
            // format can collide with the same register code, so enforce the API ID type.
            var extended = hardwareMask.FilterIdType == CanFilterIDType.Extend;
            Func<CanFrame, bool> matchesIdType = frame => frame.IsExtendedFrame == extended;
            return new ControlCanFilterPlan(rules, ControlCanFilterMode.HardwareMask,
                WithExplicitSoftwareFilter(matchesIdType, explicitSoftwareFilter),
                accCode: hardwareMask.AccCode, accMask: accMask);
        }
        if (rules.Length == 0)
        {
            return new ControlCanFilterPlan(rules, ControlCanFilterMode.None, explicitSoftwareFilter);
        }
        // Only program a range table when this binding knows the model's complete lifecycle,
        // including how to clear it. The 8E-U therefore uses a software predicate.
        if (supportsRangeTable && (options.Features & CanFeature.RangeFilter) != 0
            && rules.All(r => r is FilterRule.Range))
        {
            return new ControlCanFilterPlan(rules, ControlCanFilterMode.HardwareRanges, explicitSoftwareFilter);
        }

        var supportsHardwareMask = (options.Features & CanFeature.MaskFilter) != 0;
        var requiredFallback = (CanFeature)0;
        var masks = rules.Count(rule => rule is FilterRule.Mask);
        if (masks > 0 && (!supportsHardwareMask || masks > 1))
        {
            requiredFallback |= CanFeature.MaskFilter;
        }
        if (rules.Any(rule => rule is FilterRule.Range)
            && (!supportsRangeTable || (options.Features & CanFeature.RangeFilter) == 0))
        {
            requiredFallback |= CanFeature.RangeFilter;
        }
        if (rules.Any(rule => rule is not FilterRule.Mask and not FilterRule.Range))
        {
            requiredFallback |= CanFeature.Filters;
        }
        if (requiredFallback == 0)
        {
            // Both rule types may be supported individually, but their mixed OR still
            // cannot use either hardware path above. Authorize that combination explicitly.
            requiredFallback = CanFeature.Filters;
        }
        var missingFallback = requiredFallback & ~options.EnabledSoftwareFallback;
        if (missingFallback != 0)
        {
            throw new CanFilterConfigurationException(
                $"ControlCAN cannot express this filter list in hardware. Enable software fallback for: {missingFallback}.");
        }
        // Enabling the unsupported rule types, multiple-mask limitation or mixed combination
        // authorizes evaluating the whole OR in software, including hardware-capable rules.
        var softwareRules = new List<Func<CanFrame, bool>>(rules.Length);
        foreach (var filter in rules)
        {
            softwareRules.Add(supportsHardwareMask && filter is FilterRule.Mask mask
                ? BuildSjaMask(mask) : filter.Build());
        }
        bool MatchesAnyRule(CanFrame frame)
        {
            foreach (var rule in softwareRules)
            {
                if (rule(frame))
                {
                    return true;
                }
            }
            return false;
        }
        return new ControlCanFilterPlan(rules, ControlCanFilterMode.Software,
            WithExplicitSoftwareFilter(MatchesAnyRule, explicitSoftwareFilter));
    }

    private static Func<CanFrame, bool> WithExplicitSoftwareFilter(Func<CanFrame, bool> predicate,
        Func<CanFrame, bool>? explicitSoftwareFilter)
        => explicitSoftwareFilter == null ? predicate : frame => predicate(frame) && explicitSoftwareFilter(frame);

    // Range-only devices ignore the init mask fields (ControlCAN manual, 2.5.5/2.6.3).
    private static FilterRule.Mask? GetHardwareMask(IList<IFilterRule> rules, CanFeature features)
        => (features & CanFeature.MaskFilter) != 0 && rules.Count == 1
            ? rules[0] as FilterRule.Mask : null;

    private static uint SjaUnusedBits(CanFilterIDType idType) => idType switch
    {
        CanFilterIDType.Standard => 0x000F0000U,
        CanFilterIDType.Extend => 0x00000003U,
        _ => throw new CanFilterConfigurationException("ControlCAN mask rules require a standard or extended ID type.")
    };

    private static Func<CanFrame, bool> BuildSjaMask(FilterRule.Mask mask)
    {
        // SJA1000 6.4.15.1 / figures 9-10: single-filter register layout. Mask 1
        // means ignore. Standard frames also expose RTR and their first two data bytes.
        var comparedBits = ~(mask.AccMask | SjaUnusedBits(mask.FilterIdType));
        var extended = mask.FilterIdType == CanFilterIDType.Extend;
        return frame =>
        {
            if (frame.IsExtendedFrame != extended)
            {
                return false;
            }
            var compare = comparedBits;
            uint register;
            if (extended)
            {
                register = ((uint)frame.ID << 3) | (frame.IsRemoteFrame ? 4U : 0U);
            }
            else
            {
                register = ((uint)frame.ID << 21) | (frame.IsRemoteFrame ? 0x00100000U : 0U);
                var data = frame.Data.Span;
                // SJA1000 6.4.15.1 (page 44): absent bytes do not participate, rather than
                // comparing as zeros. This also applies to RTR frames with nonzero DLC.
                if (!frame.IsRemoteFrame && data.Length > 0)
                {
                    register |= (uint)data[0] << 8;
                }
                else
                {
                    compare &= 0xFFFF00FFU;
                }
                if (!frame.IsRemoteFrame && data.Length > 1)
                {
                    register |= data[1];
                }
                else
                {
                    compare &= 0xFFFFFF00U;
                }
            }
            return ((register ^ mask.AccCode) & compare) == 0;
        };
    }
}
