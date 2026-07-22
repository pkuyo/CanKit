using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.IsoTp;
using CanKit.Pro.Uds;

// UDS quickstart (L4, FR-UDS-001/002): a diagnostic client talks to a tiny simulated ECU
// over two ISO-TP channels on the hardware-free Virtual loopback. The ECU side here is a
// minimal responder so the sample runs standalone; the client calls are the real API.

var session = $"uds-sample-{Guid.NewGuid():N}";
using var busClient = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var busEcu = CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

using var clientChannel = IsoTp.Open(busClient, IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8));
using var ecuChannel = IsoTp.Open(busEcu, IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0));

// --- Tiny simulated ECU: answers DiagnosticSessionControl (0x10) and ReadDataByIdentifier (0x22).
var vin = "WBAEX00000EXAMPLE"u8.ToArray();
var ecuCts = new CancellationTokenSource();
var ecuTask = Task.Run(async () =>
{
    try
    {
        while (!ecuCts.IsCancellationRequested)
        {
            var request = await ecuChannel.ReceiveAsync(ecuCts.Token);
            if (request.Length == 0) continue;
            byte[]? response = request[0] switch
            {
                0x10 when request.Length >= 2 => new byte[] { 0x50, request[1] },          // session accepted
                0x22 when request.Length >= 3 && request[1] == 0xF1 && request[2] == 0x90 =>
                    new byte[] { 0x62, 0xF1, 0x90 }.Concat(vin).ToArray(),               // DID 0xF190 = VIN
                _ => new byte[] { 0x7F, request[0], 0x11 },                              // serviceNotSupported
            };
            await ecuChannel.SendAsync(response);
        }
    }
    catch (OperationCanceledException) { /* sample shutdown */ }
});

using var client = UdsClient.Create(clientChannel);
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

// Switch to the extended diagnostic session (FR-UDS-001).
await client.DiagnosticSessionControlAsync(UdsSessionType.Extended, cts.Token);
Console.WriteLine($"Session: 0x{client.CurrentSession:X2}");

// Read the VIN DID (FR-UDS-002).
var data = await client.ReadDataByIdentifierAsync(0xF190, cts.Token);
Console.WriteLine($"VIN DID 0xF190: {System.Text.Encoding.ASCII.GetString(data)}");

Console.WriteLine("Done. P2/P2* timing, NRC 0x78 response-pending and structured NRCs are handled by the client.");
ecuCts.Cancel();
