using System;
using CanKit.Pro.IsoTp;

namespace CanKit.Pro.Uds;

/// <summary>
/// Factory entry point for constructing <see cref="IUdsClient"/> instances. Non-instantiable.
/// </summary>
/// <remarks>
/// The client always wraps an existing <see cref="IIsoTpChannel"/>: transport concerns (frame
/// codec, N_As/N_Bs/N_Cr timing, addressing) stay owned by ISO-TP, the UDS client only layers
/// service semantics on top. Two ownership patterns are supported:
/// <list type="bullet">
///   <item><description><c>leaveOpen: true</c> (default) — the client borrows the channel;
///   disposing the client does not dispose the channel.</description></item>
///   <item><description><c>leaveOpen: false</c> — the client takes ownership; disposing the
///   client also disposes the underlying channel.</description></item>
/// </list>
/// </remarks>
public static class UdsClient
{
    /// <summary>
    /// Creates a new <see cref="IUdsClient"/> bound to <paramref name="channel"/>.
    /// </summary>
    /// <param name="channel">The ISO-TP channel that transports UDS requests and responses.</param>
    /// <param name="options">Client options; defaults to a fresh <see cref="UdsClientOptions"/>.</param>
    /// <param name="leaveOpen">When <c>true</c> (default) disposing the returned client does not
    /// dispose <paramref name="channel"/>; when <c>false</c> the client takes ownership.</param>
    public static IUdsClient Create(
        IIsoTpChannel channel,
        UdsClientOptions? options = null,
        bool leaveOpen = true)
    {
        if (channel is null) throw new ArgumentNullException(nameof(channel));
        return new UdsClientImpl(channel, options ?? new UdsClientOptions(), ownsChannel: !leaveOpen);
    }
}
