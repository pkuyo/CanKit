using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.Addressing;
using CanKit.Pro.J1939;

// J1939 quickstart (L4, SAE J1939): two nodes claim addresses (FR-J1939-003), then one
// emits a periodic single-frame PGN and a multi-frame PGN (auto-routed through J1939-TP,
// FR-J1939-006) while the other prints them and decodes an SPN (FR-J1939-001/002) — all
// on the hardware-free Virtual loopback.

static J1939Name MakeName(uint identity) => new(
    identityNumber: identity, manufacturerCode: 0x0AB,
    ecuInstance: 0, functionInstance: 0, function: 0x81, reserved: false,
    vehicleSystem: 0, vehicleSystemInstance: 0, industryGroup: 0, arbitraryAddressCapable: false);

var session = $"j1939-sample-{Guid.NewGuid():N}";
using var busA = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var busB = CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

using var nodeA = J1939Node.Open(busA, new J1939NodeOptions(MakeName(0x0000AA)));
using var nodeB = J1939Node.Open(busB, new J1939NodeOptions(MakeName(0x0000BB)));

await nodeA.ClaimAddressAsync(0x30);
await nodeB.ClaimAddressAsync(0x40);
Console.WriteLine($"Claimed: A=0x{nodeA.Address:X2}, B=0x{nodeB.Address:X2}");

const uint Eec1Pgn = 0xF004u; // Electronic Engine Controller 1 (carries SPN 190, engine speed)
nodeB.MessageReceived += (_, msg) =>
{
    if (msg.Pgn == Eec1Pgn)
    {
        // SPN 190 (Engine Speed): 16 bits starting at byte offset 3, 0.125 rpm/bit, offset 0.
        var engineRpm = J1939Spn.Extract(msg.Payload.Span, byteOffset: 3, startBit: 0,
            bitLength: 16, resolution: 0.125, offset: 0.0);
        Console.WriteLine($"PGN 0x{msg.Pgn:X4} from SA 0x{msg.SourceAddress:X2} " +
                          $"({msg.Payload.Length} B): engine speed = {engineRpm:F1} rpm");
    }
    else
    {
        Console.WriteLine($"PGN 0x{msg.Pgn:X4} from SA 0x{msg.SourceAddress:X2} " +
                          $"({msg.Payload.Length} B, reassembled: {BitConverter.ToString(msg.Payload.ToArray())})");
    }
};

// Encode EEC1: bytes 3..4 little-endian = 2500.0 rpm / 0.125.
var eec1 = new byte[8];
var raw = (ushort)(2500.0 / 0.125);
eec1[3] = (byte)(raw & 0xFF);
eec1[4] = (byte)(raw >> 8);
await nodeA.SendAsync(new J1939Message(Eec1Pgn, eec1, priority: 3));

// Periodic single-frame emission (FR-J1939-007, fixed-rate grid on the DeadlineScheduler).
using var periodic = nodeA.StartPeriodicSend(new J1939Message(Eec1Pgn, eec1, priority: 3),
    TimeSpan.FromMilliseconds(100));

// One multi-frame PGN (> 8 bytes) — auto-routed via J1939-TP.BAM.
var big = Enumerable.Range(0, 24).Select(i => (byte)i).ToArray();
await nodeA.SendAsync(new J1939Message(0xFEF0u, big, priority: 6));

await Task.Delay(350);
Console.WriteLine("Done.");
