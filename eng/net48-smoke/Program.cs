using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core;
using CanKit.Core.Endpoints;

try
{
    // No explicit Register() calls: registration must come from the NuGet-generated module initializer.
    if (!BusEndpointEntry.Enumerate("virtual").Any())
        throw new InvalidOperationException("The Virtual adapter was not automatically registered on .NET Framework.");

    using var sender = CanBus.Open("virtual://net48-smoke/0", options => options.Baud(500_000));
    using var receiver = CanBus.Open("virtual://net48-smoke/1", options => options.Baud(500_000));
    using var frame = CanFrame.Classic(0x123, new byte[] { 0x11, 0x22, 0x33, 0x44 });

    if (sender.Transmit(frame) != 1)
        throw new InvalidOperationException("The Virtual adapter did not transmit the frame.");

    using var received = receiver.Receive(1, timeOut: 5000).Single().CanFrame;
    if (received.ID != frame.ID || received.FrameKind != frame.FrameKind ||
        !received.Data.Span.SequenceEqual(frame.Data.Span))
        throw new InvalidOperationException("The Virtual adapter received a different frame.");

    Console.WriteLine($"net48 automatic registration and Virtual roundtrip succeeded (CLR {Environment.Version}).");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"net48 automatic registration smoke failed: {exception.Message}");
    return 1;
}
