# CanKit.Pro.IsoTp

Experimental ISO 15765-2 (ISO-TP) implementation for [CanKit](https://github.com/pkuyo/CanKit)
(CanKit.Pro). The package now ships **two halves**:

1. **Codec** — deterministic, side-effect-free builders and parsers for the four ISO-TP PCI frame
   types (Single Frame, First Frame, Consecutive Frame, Flow Control) on classic CAN and CAN-FD.
2. **Runtime (`IIsoTpChannel`)** — an actor-driven channel that composes on top of the CanKit.Pro
   L2 services (`CanKit.Pro.RawCan` demux + `SendConfirmed`, `CanKit.Pro.Actor`, `CanKit.Pro.Reliability`
   deadlines). Segments outbound PDUs into SF/FF/CFs, honors peer Flow Control (BS/STmin/Wait/
   Overflow) and enforces N_As/N_Bs/N_Cr timers, reassembles inbound PDUs (SN-checked), and delivers
   them via `ReceiveAsync` / `ReceiveAllAsync` / `DatagramReceived`.

`IsPackable=false` while the surface stabilizes and CAN-FD long-payload cases get more coverage.

## Scope

- `IsoTpFrameCodec` — bounds-safe PCI parser, `BuildSingleFrame` / `BuildFirstFrame` /
  `BuildConsecutiveFrame` / `BuildFlowControl`, correct classic-CAN vs CAN-FD DLC/capacity, correct
  First-Frame length encoding including the 32-bit CAN-FD escape form (lengths > 4095), and
  bounds-checked PCI parsing that never throws `IndexOutOfRangeException` on short frames.
- `IsoTpFrameCodec.EncodeStMin` / `DecodeStMin` — full ISO 15765-2 STmin range including the
  commonly-used 0 ms and 1 ms values (Encode) and the reserved bands `0x80..0xF0` and `0xFA..0xFF`
  which decode to 127 ms (`0x7F`) instead of throwing.
- `IsoTpFrameCodec.NextConsecutiveSequenceNumber` — Consecutive-Frame sequence numbering that
  starts at `1` after the First Frame and wraps `0..15`.
- `Pci` / `PciType` / `FlowStatus` — parsed Protocol-Control-Information view.
- `IsoTpEndpoint` / `IsoTpAddressingMode` — minimal addressing value type covering `Normal`,
  `NormalFixed`, `Extended` and `Mixed` addressing for codec purposes only.

## Runtime — `IIsoTpChannel`

- `IsoTp.Open(ICanBus, IsoTpEndpoint, IsoTpChannelOptions?)` — opens a channel that owns a private
  `CanBusService` around the supplied bus.
- `IsoTp.Open(ICanBusService, IsoTpEndpoint, IsoTpChannelOptions?, leaveOpen)` — opens a channel on
  an existing service (allows multiple ISO-TP endpoints to multiplex over the same physical bus,
  SRS FR-TP-018).
- `SendAsync(ReadOnlyMemory<byte>, CancellationToken)` — sends one PDU; task completes on TX-confirm
  of the last frame. Faults with `IsoTpTimeoutException`, `IsoTpOverflowException`,
  `IsoTpWaitFrameLimitExceededException`, or `IsoTpSendRejectedException` on the corresponding
  ISO 15765-2 error cases.
- `ReceiveAsync` / `ReceiveAllAsync` / `DatagramReceived` — three surfaces onto the same bounded,
  drop-oldest PDU inbox (bounded to `IsoTpChannelOptions.ReceiveBufferCapacity`, default 64).
- Timings: `IsoTpChannelOptions.NAs` (TX-confirm), `NBs` (peer-FC wait), `NCr` (next CF wait) and
  `WftMax` (max consecutive `Wait` FCs) are configurable; defaults are conservative 1 s / 10.

## Non-scope (yet)

- Full CAN-FD long-payload TX (>4095 bytes with the escape header) is codec-supported but not yet
  in the integration-test matrix.
- Functional (1:n) addressing (SRS FR-TP-019, Could).
- No vendor-SDK references, ever.
- Legacy `CanKit.Transport.IsoTp` remains in the tree as historical reference; new work should
  target this package.

## Fixes over the prototype (see review §1.1)

The codec is the specification-compliant replacement for `CanKit.Transport.IsoTp/Utils/FrameCodec.cs`
and deliberately avoids the following defects:

1. Inverted CAN vs CAN-FD frame kind — this codec is agnostic; it returns the frame **payload**
   bytes plus the intended CAN kind, callers construct the CAN frame with the correct kind
   (FR-TP-003).
2. Flow-Control frames now carry PCI type `0x3` (not the First-Frame nibble) (FR-TP-004).
3. Padding is applied **after** the BS/STmin bytes and never overwrites them (FR-TP-004).
4. First-Frame length high-nibble is composed with correct operator precedence
   (`((data[0] & 0x0F) << 8) | data[1]`), so lengths in `[256, 4095]` round-trip (FR-TP-005).
5. `EncodeStMin` accepts the commonly-used 0 ms and 1 ms values (FR-TP-006).
6. `DecodeStMin` maps the reserved raw values `0x80..0xF0` and `0xFA..0xFF` to 127 ms
   (`0x7F`) per ISO 15765-2 instead of throwing (FR-TP-007, FR-RAW-052).
7. Consecutive-Frame sequence numbers start at `1` and wrap `0..15` (FR-TP-008).
8. `TryParsePci` is bounds-safe and never throws `IndexOutOfRangeException`, even for a 1-byte
   frame or a truncated Flow-Control frame (FR-TP-007).
9. Classic-CAN single frames are always ≤ 8 bytes (FR-TP-015).

Status: pre-release (0.1.x), codec-only.
