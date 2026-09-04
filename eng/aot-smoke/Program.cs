using CanKit.Core;
using CanKit.Core.Endpoints;

if (!BusEndpointEntry.Enumerate("virtual").Any())
    throw new InvalidOperationException("The Virtual adapter was not registered.");

using var bus = CanBus.Open("virtual://aot/0", options => options.Baud(500_000));
Console.WriteLine("NativeAOT static registration succeeded.");
