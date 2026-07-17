using System;
using System.Collections.Generic;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// A registry of <see cref="IHaweCodec"/> factories keyed by codec <see cref="IHaweCodec.Name"/>
    /// (SRS FR-HAWE-001). Its role is analogous to the former ISO-TP register SPI: a
    /// public discovery/registration surface that lets an application (or a private HAWE module
    /// assembly) plug in a proprietary codec without touching the framework itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registrations hold codec <b>factories</b>, not codec instances, because a channel opens on
    /// exactly one bus and must not share codec state across buses. A factory is invoked once per
    /// <see cref="HaweChannel"/> and hands back a fresh codec bound to that channel's lifetime.
    /// </para>
    /// <para>
    /// The registry is deliberately in-process and dependency-free: it does not scan assemblies
    /// or use MEF-style attributes. A private HAWE module registers its codec by calling
    /// <see cref="Register(string, Func{IHaweCodec})"/> once at application startup, which keeps
    /// every reference to HAWE-proprietary detail inside the private module.
    /// </para>
    /// </remarks>
    public interface IHaweCodecRegistry
    {
        /// <summary>
        /// Registers a codec factory under <paramref name="name"/>. Overwrites any previous
        /// factory registered under the same name -- this mirrors the "last writer wins" behaviour
        /// of the other registries in CanKit and lets tests substitute a codec without having to
        /// unregister an earlier one first.
        /// </summary>
        /// <param name="name">Codec name; must be non-null/non-empty and unique per registry.</param>
        /// <param name="factory">Factory producing a fresh codec instance per invocation.</param>
        void Register(string name, Func<IHaweCodec> factory);

        /// <summary>
        /// Removes the factory previously registered under <paramref name="name"/>, if any.
        /// Returns true when a registration was removed, false when there was none. Never throws.
        /// </summary>
        bool Unregister(string name);

        /// <summary>
        /// Looks up the factory registered under <paramref name="name"/> and invokes it, returning
        /// a fresh codec instance. Throws <see cref="KeyNotFoundException"/> when no such
        /// registration exists -- callers that want a "try" pattern should first check
        /// <see cref="IsRegistered(string)"/>.
        /// </summary>
        IHaweCodec Create(string name);

        /// <summary>
        /// True when a factory is registered under <paramref name="name"/>.
        /// </summary>
        bool IsRegistered(string name);

        /// <summary>
        /// Snapshot of every currently-registered codec name, in unspecified order. Primarily for
        /// diagnostics and tests; the framework itself never enumerates the registry on the hot
        /// path.
        /// </summary>
        IReadOnlyList<string> RegisteredNames { get; }
    }
}
