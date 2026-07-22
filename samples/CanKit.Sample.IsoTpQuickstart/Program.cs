using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.IsoTp;

// ISO-TP quickstart (L3, FR-TP-001/002): two channels on the hardware-free Virtual
// loopback exchange a single-frame and a multi-frame PDU — the same call pattern that
// works unchanged against any of the vendor adapters (PCAN, Kvaser, SocketCAN, ...).

var session = $"isotp-sample-{Guid.NewGuid():N}";
using var busA = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var busB = CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

// Normal addressing: A transmits on 0x7E0 and receives on 0x7E8; B mirrors.
using var sender = IsoTp.Open(busA, IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8));
using var receiver = IsoTp.Open(busB, IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0));

var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

// 1) Single Frame (≤ 7 payload bytes on classic CAN).
var receiveSf = receiver.ReceiveAsync(cts.Token);
byte[] sfPdu = { 0x22, 0xF1, 0x90 }; // e.g. UDS ReadDataByIdentifier(0xF190)
await sender.SendAsync(sfPdu, cts.Token);
Console.WriteLine($"SF  received: {BitConverter.ToString(await receiveSf)}");

// 2) Multi Frame: 200 bytes are segmented FF -> FC -> CFs and reassembled for you.
var pdu = Enumerable.Range(0, 200).Select(i => (byte)(i & 0xFF)).ToArray();
var receiveMf = receiver.ReceiveAsync(cts.Token);
await sender.SendAsync(pdu, cts.Token);
var got = await receiveMf;
Console.WriteLine($"MF  received: {got.Length} bytes, identical: {got.SequenceEqual(pdu)}");

Console.WriteLine("Done. Timeouts (N_As/N_Bs/N_Cr) and peer flow control are enforced by the channel.");
