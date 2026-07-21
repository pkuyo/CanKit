using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Endpoints;
using CanKit.Pro.Actor;
using CanKit.Pro.Addressing;
using CanKit.Pro.RawCan;
using CanKit.Pro.Reliability;

Console.WriteLine(typeof(CanFrame).FullName);
Console.WriteLine(typeof(CanBus).FullName);
Console.WriteLine(typeof(BusEndpointEntry).FullName);
Console.WriteLine(typeof(ProtocolActor).FullName);
Console.WriteLine(typeof(J1939Pgn).FullName);
Console.WriteLine(typeof(ICanBusService).FullName);
Console.WriteLine(typeof(DeadlineScheduler).FullName);

using var bus = CanBus.Open("virtual://alpha/0", cfg => cfg.Baud(500_000));
