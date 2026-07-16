# CanKit.Pro.IsoTp

Experimental **codec foundation** for ISO 15765-2 (ISO-TP) inside [CanKit](https://github.com/pkuyo/CanKit)
(CanKit.Pro). Deterministic, side-effect-free builders and parsers for the four ISO-TP PCI frame
types (Single Frame, First Frame, Consecutive Frame, Flow Control) on classic CAN and CAN-FD.

This is the **pure protocol half** of the ISO-TP rewrite; a scheduler, channel and runtime will
follow in a later release and will build on top of this codec. `IsPackable=false` for now.

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

## Non-scope (yet)

- No `IIsoTpChannel`, no `SendAsync`, no scheduler, no actor wiring.
- No adapter or vendor-SDK references.
- Existing prototype `CanKit.Transport.IsoTp` is left untouched; this assembly is intended to
  supersede its codec once the runtime layer lands here.

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
10. `BuildSingleFrame` rejects a zero-length payload at build time: ISO 15765-2 does not define a
    Single Frame with `SF_DL == 0`, so producing such a frame would yield bytes no conformant peer
    could parse (bugbot 3594958440).
11. `TryParsePci` requires an `isCanFd` argument so the Single-Frame escape header (`0x00 LEN …`)
    and the First-Frame escape header (`0x10 0x00 LEN[4] …`) are only accepted on CAN-FD frames;
    on classic CAN those bit-patterns are invalid and are rejected instead of being mis-parsed as
    escape headers (bugbot 3594958440 / 3594958445).

Status: pre-release (0.1.x), codec-only.
