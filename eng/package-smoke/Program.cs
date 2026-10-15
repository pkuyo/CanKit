using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Core.Endpoints;

#if CANKIT_LOCAL_PROJECT_REFERENCES
// ProjectReference builds do not import the NuGet buildTransitive registration assets.
CanKit.Adapter.Virtual.CanKitRegistration.Register();
#endif

Console.WriteLine(typeof(CanFrame).FullName);
Console.WriteLine(typeof(CanBus).FullName);
Console.WriteLine(typeof(BusEndpointEntry).FullName);

using var bus = CanBus.Open("virtual://alpha/0", cfg => cfg.Baud(500_000));
