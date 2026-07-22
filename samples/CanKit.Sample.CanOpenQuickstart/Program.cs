using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.CANopen;
using CanKit.Pro.CANopen.Nmt;
using CanKit.Pro.CANopen.Pdo;

// CANopen quickstart (L4, CiA 301): a master writes a value into a slave's OD over SDO
// (FR-CO-002), brings both nodes to Operational via NMT (FR-CO-007), and the slave's
// event-timer TPDO (FR-CO-006) ships the mapped value to the master's RPDO — all on the
// hardware-free Virtual loopback.

var session = $"canopen-sample-{Guid.NewGuid():N}";
using var busMaster = CanBus.Open($"virtual://{session}/0", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));
using var busSlave = CanBus.Open($"virtual://{session}/1", cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(500_000));

using var master = CanOpen.OpenNode(busMaster, nodeId: 0x01);
using var slave = CanOpen.OpenNode(busSlave, nodeId: 0x11);

// Slave OD: a 16-bit process value; its TPDO1 (default COB-ID) ships it every 100 ms.
slave.ObjectDictionary.AddU16(0x2000, 0x00, 0x0000);
slave.ConfigureTpdo(1, new PdoMapping().Add(0x2000, 0x00, bitLength: 16),
    transmission: TpdoTransmission.EventTimer, eventTimerInterval: TimeSpan.FromMilliseconds(100));

// Master RPDO: unpack the same layout into a local OD entry and print each reception.
master.ObjectDictionary.AddU16(0x2100, 0x00, 0x0000);
master.ConfigureRpdo(1, new PdoMapping().Add(0x2100, 0x00, bitLength: 16),
    cobId: CanOpenCobId.TpdoDefault(nodeId: 0x11, pdoIndex: 1));
master.RpdoReceived += (_, e) =>
    Console.WriteLine($"RPDO on 0x{e.CobId:X3}: {BitConverter.ToString(e.Payload)} " +
                      $"(OD 0x2100 = 0x{master.ObjectDictionary.ReadUnsigned(0x2100, 0x00):X4})");

// SDO expedited write + read-back of the process value.
await master.SdoDownloadAsync(serverNodeId: 0x11, index: 0x2000, subindex: 0x00, new byte[] { 0x34, 0x12 });
var raw = await master.SdoUploadAsync(serverNodeId: 0x11, index: 0x2000, subindex: 0x00);
Console.WriteLine($"SDO roundtrip: wrote 0x1234, read back {BitConverter.ToString(raw)}");

// NMT: bring both to Operational (PDO only flows in Operational).
await master.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x11);
await slave.SendNmtCommandAsync(NmtCommand.Start, targetNodeId: 0x01);

Console.WriteLine("Watching TPDO emissions for ~400 ms ...");
await Task.Delay(400);
Console.WriteLine("Done.");
