using System.Collections.Generic;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Adapter.ControlCAN.Definitions;
using CanKit.Adapter.ControlCAN.Diagnostics;
using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;
using CcApi = CanKit.Adapter.ControlCAN.Native.ControlCAN;

namespace CanKit.Adapter.ControlCAN;

/// <summary>Programs and clears range tables using the model's documented reference commands.</summary>
internal sealed class ControlCanRangeFilterTable(ControlCanBus bus, ControlCanDevice device)
{
    private readonly RangeFilterCommands? _commands = RangeFilterCommands.ForDevice(bus.DevType);

    public bool IsSupported => _commands != null;

    public unsafe void Program(IEnumerable<FilterRule.Range> ranges)
    {
        var commands = _commands ?? throw new CanFilterConfigurationException(
            "ControlCAN cannot program a range table on this device model.");
        // Remember partially staged tables as well as successfully activated filters.
        // The caller attempts cleanup on failure; only a successful clear removes the marker.
        device.SetRangeFilterState(bus.CanIndex, true);
        var pRecord = stackalloc CcApi.VCI_FILTER_RECORD[1];
        foreach (var range in ranges)
        {
            pRecord->Start = range.From;
            pRecord->End = range.To;
            pRecord->ExtFrame = range.FilterIdType == CanFilterIDType.Extend ? 1U : 0U;
            ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(bus.RawDevType, bus.DevIndex, bus.CanIndex,
                commands.Add, pRecord), "VCI_SetReference(Filter)", bus);
        }
        ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(bus.RawDevType, bus.DevIndex, bus.CanIndex,
            commands.Start, null), "VCI_SetReference(StartFilter)", bus);
    }

    public unsafe void Clear()
    {
        // Fresh channels do not depend on optional clear commands. Known stale tables
        // must be cleared even when the managed device was closed and recreated.
        if (!device.HasRangeFilters(bus.CanIndex))
        {
            return;
        }
        var commands = _commands ?? throw new CanFilterConfigurationException(
            "ControlCAN cannot clear a previously configured range table on this device model.");
        if (commands.Stop.HasValue)
        {
            ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(bus.RawDevType, bus.DevIndex, bus.CanIndex,
                commands.Stop.Value, null), "VCI_SetReference(StopFilter)", bus);
        }
        ControlCanErr.ThrowIfErr(CcApi.VCI_SetReference(bus.RawDevType, bus.DevIndex, bus.CanIndex,
            commands.Clear, null), "VCI_SetReference(ClearFilter)", bus);
        device.SetRangeFilterState(bus.CanIndex, false);
    }

    private sealed class RangeFilterCommands(uint add, uint start, uint? stop, uint clear)
    {
        public uint Add { get; } = add;
        public uint Start { get; } = start;
        public uint? Stop { get; } = stop;
        public uint Clear { get; } = clear;

        // ControlCAN manual 2.6.8: no range-table commands are assumed for other models.
        public static RangeFilterCommands? ForDevice(ControlCanDeviceKind deviceType) => deviceType switch
        {
            ControlCanDeviceKind.VCI_USBCAN_E_U or ControlCanDeviceKind.VCI_USBCAN_2E_U
                => new RangeFilterCommands(add: 1, start: 2, stop: null, clear: 3),
            ControlCanDeviceKind.VCI_USBCAN_4E_U
                => new RangeFilterCommands(add: 7, start: 8, stop: 9, clear: 10),
            _ => null
        };
    }
}
