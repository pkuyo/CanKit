using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Event data raised by <see cref="IIsoTpChannel.DatagramReceived"/> after a full ISO-TP PDU
/// (Single-Frame, or First-Frame + all Consecutive Frames) has been reassembled.
/// </summary>
public sealed class IsoTpDatagramReceivedEventArgs : EventArgs
{
    /// <summary>The reassembled PDU bytes. The array is owned by the channel and callers must
    /// not mutate it; it is safe to keep a reference until the next event or PDU dequeue.</summary>
    public byte[] Data { get; }

    /// <summary>The endpoint that produced this PDU (matches <see cref="IIsoTpChannel.Endpoint"/>).</summary>
    public IsoTpEndpoint Endpoint { get; }

    /// <summary>Creates a new event args instance.</summary>
    public IsoTpDatagramReceivedEventArgs(IsoTpEndpoint endpoint, byte[] data)
    {
        Endpoint = endpoint;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }
}
