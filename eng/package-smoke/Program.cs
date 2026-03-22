using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core;
using CanKit.Core.Endpoints;

Console.WriteLine(typeof(CanFrame).FullName);
Console.WriteLine(typeof(CanBus).FullName);
Console.WriteLine(typeof(BusEndpointEntry).FullName);

using var bus = CanBus.Open("virtual://alpha/0", cfg => cfg.TimingClassic(500_000));