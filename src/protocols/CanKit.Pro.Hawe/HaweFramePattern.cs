using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Pro.RawCan;

namespace CanKit.Pro.Hawe
{
    /// <summary>
    /// A generic, protocol-agnostic frame-selection pattern that tells a
    /// <see cref="IHaweChannel"/> which CAN frames belong to a proprietary HAWE codec instance
    /// (SRS FR-HAWE-002). This is the only frame-shape information the public framework holds:
    /// the actual codec is free to interpret matched frames however it wants -- the framework
    /// merely delivers them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pattern is expressed as a <see cref="CanIdFilter"/> (ID range or acceptance code/mask):
    /// the same allocation-free filter primitive used by every other L2/L3 protocol instance
    /// (ISO-TP, J1939-TP, CANopen, ...), routed through the shared
    /// <see cref="ICanBusService"/> demultiplexer so this codec's frames are delivered on its own
    /// bounded, filtered subscription without the codec having to fight over
    /// <c>ICanBus.ReceiveAsync</c> with other stacks.
    /// </para>
    /// <para>
    /// The pattern deliberately carries only <b>which</b> frames matter, not <b>what they mean</b>:
    /// no payload layout, no service-ID lookup table, no frame-shape assumptions beyond a CAN-ID
    /// selector. This is what keeps the framework free of any HAWE proprietary detail (SRS CON-006 /
    /// A-6): the concrete decode/encode logic lives entirely inside the private
    /// <see cref="IHaweCodec"/> implementation shipped in a separate, non-public repository.
    /// </para>
    /// </remarks>
    public readonly struct HaweFramePattern
    {
        /// <summary>
        /// Creates a pattern that accepts every frame matching <paramref name="filter"/>.
        /// </summary>
        /// <param name="filter">
        /// The ID-range or acceptance-code/mask filter that selects this codec's frames on the
        /// shared bus. Evaluated on the demultiplexer's per-frame fast path; see
        /// <see cref="CanIdFilter"/> for the exact matching semantics.
        /// </param>
        public HaweFramePattern(CanIdFilter filter)
        {
            Filter = filter;
        }

        /// <summary>
        /// The CAN-ID filter that decides which frames on the shared bus belong to this codec
        /// instance. Deliberately opaque about payload semantics: the framework never inspects
        /// frame data itself.
        /// </summary>
        public CanIdFilter Filter { get; }

        /// <summary>
        /// Convenience factory for an inclusive ID-range pattern.
        /// </summary>
        /// <param name="from">Minimum ID, inclusive.</param>
        /// <param name="to">Maximum ID, inclusive.</param>
        /// <param name="idType">Standard 11-bit or extended 29-bit ID space.</param>
        public static HaweFramePattern Range(
            uint from,
            uint to,
            CanFilterIDType idType = CanFilterIDType.Standard)
            => new(CanIdFilter.Range(from, to, idType));

        /// <summary>
        /// Convenience factory for an acceptance-code/mask pattern.
        /// </summary>
        /// <param name="accCode">Acceptance code.</param>
        /// <param name="accMask">Acceptance mask; only the set bits are compared.</param>
        /// <param name="idType">Standard 11-bit or extended 29-bit ID space.</param>
        public static HaweFramePattern Mask(
            uint accCode,
            uint accMask,
            CanFilterIDType idType = CanFilterIDType.Standard)
            => new(CanIdFilter.Mask(accCode, accMask, idType));
    }
}
