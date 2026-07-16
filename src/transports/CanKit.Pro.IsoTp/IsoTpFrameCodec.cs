using System;

namespace CanKit.Pro.IsoTp;

/// <summary>
/// Deterministic, side-effect-free codec for ISO 15765-2 (ISO-TP) Protocol-Control-Information
/// frames on classic CAN and CAN-FD. This assembly deliberately contains no scheduler, no channel,
/// no runtime and no vendor-adapter reference; every method here is a pure function over spans
/// and value types.
/// </summary>
/// <remarks>
/// <para>
/// The codec is agnostic about how the produced payload will be wrapped in a
/// <c>CanFrame</c> — callers know whether they want classic CAN or CAN-FD and construct the
/// frame with the appropriate factory (this avoids the inverted-frame-kind defect of the earlier
/// prototype; see FR-TP-003).
/// </para>
/// <para>Frame-length capacity is controlled by the <c>isCanFd</c> flag passed to the builders:
/// classic CAN yields at most 8 bytes on the wire; CAN-FD yields up to 64 bytes and pads to the
/// next valid CAN-FD DLC step (8, 12, 16, 20, 24, 32, 48, 64).</para>
/// </remarks>
public static class IsoTpFrameCodec
{
    /// <summary>Maximum CAN data-length for a classic-CAN frame (8 bytes).</summary>
    public const int ClassicCanMaxData = 8;

    /// <summary>Maximum CAN data-length for a CAN-FD frame (64 bytes).</summary>
    public const int CanFdMaxData = 64;

    /// <summary>Sequence number the first Consecutive Frame after a First Frame must carry.</summary>
    public const byte FirstConsecutiveSequenceNumber = 1;

    /// <summary>Modulus for the CF sequence number (SN wraps 0..15).</summary>
    public const int SequenceNumberModulus = 16;

    /// <summary>Maximum length that fits in the 12-bit classic First-Frame length field.</summary>
    public const int MaxClassicFirstFrameLength = 0xFFF; // 4095

    /// <summary>
    /// Maximum length that fits in the 32-bit CAN-FD First-Frame escape length field.
    /// </summary>
    public const uint MaxFdFirstFrameLength = 0xFFFF_FFFFu;

    /// <summary>
    /// Default padding byte per ISO 15765-2 recommendation (`0xCC` is a common choice; `0xAA` and
    /// `0x00` are also seen). Only used when the caller requests padding.
    /// </summary>
    public const byte DefaultPaddingByte = 0xCC;

    // -------------------------------------------------------------------------------------------
    // Capacity helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the maximum number of user-data bytes that fit into a Single Frame on a given
    /// frame-kind and addressing mode.
    /// </summary>
    /// <param name="isCanFd"><c>true</c> for CAN-FD (up to 64-byte frames), <c>false</c> for classic CAN.</param>
    /// <param name="usesAddressExtension"><c>true</c> when the endpoint burns the first payload
    /// byte for an address-extension byte.</param>
    /// <remarks>
    /// Classic-CAN SF uses a 1-byte PCI, so the classic capacity is
    /// <c>8 - 1 - (extension ? 1 : 0)</c>. CAN-FD SF supports the same 1-byte PCI plus a 2-byte
    /// escape form (`PCI = 0x00`, `LEN` byte) that unlocks the full CAN-FD payload up to
    /// <c>64 - 2 - (extension ? 1 : 0)</c>.
    /// </remarks>
    public static int SingleFrameMaxDataLength(bool isCanFd, bool usesAddressExtension)
    {
        int frameSize = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        int pci = isCanFd ? 2 : 1;
        int addrExt = usesAddressExtension ? 1 : 0;
        return frameSize - pci - addrExt;
    }

    /// <summary>
    /// Returns the maximum number of Single-Frame data bytes that still fit into the classic
    /// 1-byte PCI form (SF_DL in the low nibble, 1..7). Beyond this and up to
    /// <see cref="SingleFrameMaxDataLength"/> the CAN-FD escape form (`0x00 LEN`) must be used.
    /// </summary>
    public static int SingleFrameShortFormMaxDataLength(bool usesAddressExtension)
    {
        int addrExt = usesAddressExtension ? 1 : 0;
        return ClassicCanMaxData - 1 - addrExt;
    }

    /// <summary>
    /// Returns the number of user-data bytes carried in the First Frame of a segmented PDU.
    /// </summary>
    /// <param name="isCanFd"><c>true</c> for CAN-FD, <c>false</c> for classic CAN.</param>
    /// <param name="usesAddressExtension"><c>true</c> when the endpoint burns the first payload
    /// byte for an address-extension byte.</param>
    /// <param name="useLongLength"><c>true</c> when the FF must carry the 32-bit CAN-FD escape
    /// length (for total lengths &gt; 4095); ignored for classic CAN.</param>
    /// <remarks>
    /// Classic-CAN FF has a 2-byte PCI, so the classic capacity is <c>8 - 2 - extension</c>
    /// (6 bytes without extension). CAN-FD FF uses the same 2-byte PCI for lengths up to 4095, or
    /// a 6-byte PCI (`FF nibble | 0x00`, `0x00`, then a 32-bit length) when
    /// <paramref name="useLongLength"/> is <c>true</c>.
    /// </remarks>
    public static int FirstFrameMaxDataLength(bool isCanFd, bool usesAddressExtension, bool useLongLength)
    {
        int frameSize = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        int pci = useLongLength ? 6 : 2;
        int addrExt = usesAddressExtension ? 1 : 0;
        return frameSize - pci - addrExt;
    }

    /// <summary>
    /// Returns the maximum number of user-data bytes carried in a Consecutive Frame. CF uses a
    /// 1-byte PCI on both classic CAN and CAN-FD.
    /// </summary>
    public static int ConsecutiveFrameMaxDataLength(bool isCanFd, bool usesAddressExtension)
    {
        int frameSize = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        int addrExt = usesAddressExtension ? 1 : 0;
        return frameSize - 1 - addrExt;
    }

    /// <summary>
    /// Returns the next valid CAN-FD DLC data length that is greater than or equal to
    /// <paramref name="dataLength"/>. Classic CAN always pads to 8 bytes.
    /// </summary>
    /// <param name="dataLength">Number of user bytes that must fit (0..64 for CAN-FD, 0..8 for classic).</param>
    /// <param name="isCanFd"><c>true</c> to use the CAN-FD DLC ladder, <c>false</c> to pad to 8.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dataLength"/> is negative, exceeds 8 for classic CAN, or exceeds 64 for CAN-FD.
    /// </exception>
    public static int NextValidFrameLength(int dataLength, bool isCanFd)
    {
        if (dataLength < 0)
            throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength, "Frame data length must be non-negative.");
        if (!isCanFd)
        {
            if (dataLength > ClassicCanMaxData)
                throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength,
                    "Classic-CAN frames cannot exceed 8 data bytes.");
            return ClassicCanMaxData;
        }
        if (dataLength > CanFdMaxData)
            throw new ArgumentOutOfRangeException(nameof(dataLength), dataLength,
                "CAN-FD frames cannot exceed 64 data bytes.");
        return dataLength switch
        {
            <= 8 => 8,
            <= 12 => 12,
            <= 16 => 16,
            <= 20 => 20,
            <= 24 => 24,
            <= 32 => 32,
            <= 48 => 48,
            _ => 64
        };
    }

    /// <summary>
    /// Returns the next CF sequence number after <paramref name="current"/> (wraps 0..15).
    /// </summary>
    public static byte NextConsecutiveSequenceNumber(byte current)
        => (byte)((current + 1) & 0x0F);

    // -------------------------------------------------------------------------------------------
    // Frame builders
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes a Single-Frame CAN payload for the given <paramref name="userData"/> into a freshly
    /// allocated <see cref="byte"/>[] and returns it. See <see cref="BuildSingleFrame(Span{byte}, in IsoTpEndpoint, ReadOnlySpan{byte}, bool, bool, byte)"/>
    /// for the span-based, allocation-free variant.
    /// </summary>
    public static byte[] BuildSingleFrame(in IsoTpEndpoint endpoint, ReadOnlySpan<byte> userData,
        bool isCanFd, bool padding, byte paddingByte = DefaultPaddingByte)
    {
        int maxLen = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        Span<byte> buffer = stackalloc byte[CanFdMaxData];
        var slice = buffer.Slice(0, maxLen);
        int written = BuildSingleFrame(slice, endpoint, userData, isCanFd, padding, paddingByte);
        return slice.Slice(0, written).ToArray();
    }

    /// <summary>
    /// Writes a Single-Frame CAN payload for the given <paramref name="userData"/> into
    /// <paramref name="destination"/>. Returns the actual number of bytes written; the destination
    /// buffer must be at least <see cref="ClassicCanMaxData"/> (or <see cref="CanFdMaxData"/> for
    /// CAN-FD) bytes long.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 15765-2 does not define an empty Single Frame: on classic CAN the SF_DL low-nibble range
    /// is <c>1..7</c>, and on CAN-FD the escape-form <c>LEN</c> byte likewise starts at <c>1</c>.
    /// A zero-length SF is therefore rejected at build time to prevent producing an on-wire frame
    /// that no conformant peer could parse (fixes bugbot 3594958440).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="userData"/> is empty, exceeds the Single-Frame capacity for the requested
    /// frame-kind/addressing combination, or <paramref name="destination"/> is too small.
    /// </exception>
    public static int BuildSingleFrame(Span<byte> destination, in IsoTpEndpoint endpoint,
        ReadOnlySpan<byte> userData, bool isCanFd, bool padding,
        byte paddingByte = DefaultPaddingByte)
    {
        int addrExt = endpoint.AddressExtensionSize;
        int maxFrame = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        int shortMax = SingleFrameShortFormMaxDataLength(endpoint.UsesAddressExtension);
        int longMax = SingleFrameMaxDataLength(isCanFd, endpoint.UsesAddressExtension);

        if (userData.Length < 1 || userData.Length > longMax)
            throw new ArgumentOutOfRangeException(nameof(userData), userData.Length,
                $"Single-Frame user data length must be in [1, {longMax}] for this endpoint/frame kind.");
        if (destination.Length < maxFrame)
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length,
                $"Destination buffer must be at least {maxFrame} bytes for the requested frame kind.");

        bool useEscape = userData.Length > shortMax;
        int pciBytes = useEscape ? 2 : 1;
        int payloadLen = addrExt + pciBytes + userData.Length;

        if (endpoint.UsesAddressExtension)
            destination[0] = endpoint.AddressExtension;

        int pciIndex = addrExt;
        if (useEscape)
        {
            // CAN-FD escape form: PCI = 0x00, LEN = userData.Length (up to 62 or 61-with-extension)
            destination[pciIndex] = (byte)PciType.SingleFrame << 4; // 0x00
            destination[pciIndex + 1] = (byte)userData.Length;
        }
        else
        {
            destination[pciIndex] = (byte)(((byte)PciType.SingleFrame << 4) | (userData.Length & 0x0F));
        }

        int dataStart = addrExt + pciBytes;
        userData.CopyTo(destination.Slice(dataStart, userData.Length));

        int frameLen = padding ? NextValidFrameLength(payloadLen, isCanFd) : payloadLen;
        if (frameLen > payloadLen)
            destination.Slice(payloadLen, frameLen - payloadLen).Fill(paddingByte);

        return frameLen;
    }

    /// <summary>
    /// Writes a First-Frame CAN payload announcing a segmented PDU of
    /// <paramref name="totalLength"/> bytes and containing the first <paramref name="firstChunk"/>
    /// user bytes. Allocates and returns a freshly sized <see cref="byte"/>[].
    /// </summary>
    public static byte[] BuildFirstFrame(in IsoTpEndpoint endpoint, int totalLength,
        ReadOnlySpan<byte> firstChunk, bool isCanFd)
    {
        int maxLen = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        Span<byte> buffer = stackalloc byte[CanFdMaxData];
        var slice = buffer.Slice(0, maxLen);
        int written = BuildFirstFrame(slice, endpoint, totalLength, firstChunk, isCanFd);
        return slice.Slice(0, written).ToArray();
    }

    /// <summary>
    /// Writes a First-Frame CAN payload into <paramref name="destination"/>. First Frames never
    /// use padding — they always fill the underlying CAN frame completely.
    /// </summary>
    /// <remarks>
    /// User bytes copied from <paramref name="firstChunk"/> are capped to
    /// <c>min(firstChunk.Length, frame capacity, totalLength)</c> so the on-wire data never
    /// exceeds the PDU size announced in the FF_DL field (fixes bugbot 3596393504).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="totalLength"/> is negative or exceeds the addressable range for the
    /// requested frame kind; classic-CAN cannot address totals &gt; 4095 (FF escape form is
    /// CAN-FD-only per ISO 15765-2).
    /// </exception>
    public static int BuildFirstFrame(Span<byte> destination, in IsoTpEndpoint endpoint,
        int totalLength, ReadOnlySpan<byte> firstChunk, bool isCanFd)
    {
        if (totalLength < 0)
            throw new ArgumentOutOfRangeException(nameof(totalLength), totalLength,
                "Total PDU length must be non-negative.");
        int addrExt = endpoint.AddressExtensionSize;
        int maxFrame = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        bool useLongLength = totalLength > MaxClassicFirstFrameLength;
        if (useLongLength && !isCanFd)
            throw new ArgumentOutOfRangeException(nameof(totalLength), totalLength,
                "Classic-CAN First Frames cannot encode a total length greater than 4095; use CAN-FD.");

        if (destination.Length < maxFrame)
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length,
                $"Destination buffer must be at least {maxFrame} bytes for the requested frame kind.");

        int pciBytes = useLongLength ? 6 : 2;
        int dataCapacity = maxFrame - addrExt - pciBytes;
        // Cap to totalLength as well: otherwise a long firstChunk would place more user bytes in
        // the frame than FF_DL announces, so peers / TryParsePci disagree on how much of the
        // payload is real data vs trailing fill (fixes bugbot 3596393504).
        int dataLen = Math.Min(firstChunk.Length, Math.Min(dataCapacity, totalLength));

        if (endpoint.UsesAddressExtension)
            destination[0] = endpoint.AddressExtension;

        int pciIndex = addrExt;
        if (useLongLength)
        {
            destination[pciIndex] = (byte)PciType.FirstFrame << 4; // 0x10
            destination[pciIndex + 1] = 0x00;
            uint len = (uint)totalLength;
            destination[pciIndex + 2] = (byte)((len >> 24) & 0xFF);
            destination[pciIndex + 3] = (byte)((len >> 16) & 0xFF);
            destination[pciIndex + 4] = (byte)((len >> 8) & 0xFF);
            destination[pciIndex + 5] = (byte)(len & 0xFF);
        }
        else
        {
            destination[pciIndex] =
                (byte)(((byte)PciType.FirstFrame << 4) | ((totalLength >> 8) & 0x0F));
            destination[pciIndex + 1] = (byte)(totalLength & 0xFF);
        }

        int dataStart = addrExt + pciBytes;
        firstChunk.Slice(0, dataLen).CopyTo(destination.Slice(dataStart, dataLen));

        // First Frames always fill the underlying CAN frame; unused trailing bytes (only possible
        // when the caller supplied a short chunk) are zero-padded so the on-wire frame is well-defined.
        int payloadLen = dataStart + dataLen;
        if (payloadLen < maxFrame)
            destination.Slice(payloadLen, maxFrame - payloadLen).Clear();

        return maxFrame;
    }

    /// <summary>
    /// Writes a Consecutive-Frame CAN payload with sequence number <paramref name="sequenceNumber"/>
    /// (0..15) and the given data chunk. Allocates and returns a freshly sized <see cref="byte"/>[].
    /// </summary>
    public static byte[] BuildConsecutiveFrame(in IsoTpEndpoint endpoint, byte sequenceNumber,
        ReadOnlySpan<byte> chunk, bool isCanFd, bool padding, byte paddingByte = DefaultPaddingByte)
    {
        int maxLen = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        Span<byte> buffer = stackalloc byte[CanFdMaxData];
        var slice = buffer.Slice(0, maxLen);
        int written = BuildConsecutiveFrame(slice, endpoint, sequenceNumber, chunk, isCanFd,
            padding, paddingByte);
        return slice.Slice(0, written).ToArray();
    }

    /// <summary>
    /// Writes a Consecutive-Frame CAN payload into <paramref name="destination"/>. Returns the
    /// actual number of bytes written.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The chunk does not fit in a Consecutive Frame for the requested frame kind / addressing,
    /// or <paramref name="destination"/> is too small.
    /// </exception>
    public static int BuildConsecutiveFrame(Span<byte> destination, in IsoTpEndpoint endpoint,
        byte sequenceNumber, ReadOnlySpan<byte> chunk, bool isCanFd, bool padding,
        byte paddingByte = DefaultPaddingByte)
    {
        int addrExt = endpoint.AddressExtensionSize;
        int maxFrame = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        int maxData = ConsecutiveFrameMaxDataLength(isCanFd, endpoint.UsesAddressExtension);

        if (chunk.Length < 0 || chunk.Length > maxData)
            throw new ArgumentOutOfRangeException(nameof(chunk), chunk.Length,
                $"Consecutive-Frame chunk length must be in [0, {maxData}] for this endpoint/frame kind.");
        if (destination.Length < maxFrame)
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length,
                $"Destination buffer must be at least {maxFrame} bytes for the requested frame kind.");

        if (endpoint.UsesAddressExtension)
            destination[0] = endpoint.AddressExtension;

        int pciIndex = addrExt;
        destination[pciIndex] = (byte)(((byte)PciType.ConsecutiveFrame << 4) | (sequenceNumber & 0x0F));

        int dataStart = pciIndex + 1;
        chunk.CopyTo(destination.Slice(dataStart, chunk.Length));

        int payloadLen = dataStart + chunk.Length;
        int frameLen = padding ? NextValidFrameLength(payloadLen, isCanFd) : payloadLen;
        if (frameLen > payloadLen)
            destination.Slice(payloadLen, frameLen - payloadLen).Fill(paddingByte);

        return frameLen;
    }

    /// <summary>
    /// Writes a Flow-Control CAN payload with the given <paramref name="flowStatus"/>,
    /// <paramref name="blockSize"/> and <paramref name="stMinRaw"/>. Allocates and returns a
    /// freshly sized <see cref="byte"/>[].
    /// </summary>
    public static byte[] BuildFlowControl(in IsoTpEndpoint endpoint, FlowStatus flowStatus,
        byte blockSize, byte stMinRaw, bool isCanFd, bool padding,
        byte paddingByte = DefaultPaddingByte)
    {
        int maxLen = isCanFd ? CanFdMaxData : ClassicCanMaxData;
        Span<byte> buffer = stackalloc byte[CanFdMaxData];
        var slice = buffer.Slice(0, maxLen);
        int written = BuildFlowControl(slice, endpoint, flowStatus, blockSize, stMinRaw, isCanFd,
            padding, paddingByte);
        return slice.Slice(0, written).ToArray();
    }

    /// <summary>
    /// Writes a Flow-Control CAN payload into <paramref name="destination"/>. Returns the actual
    /// number of bytes written. Padding is applied <em>after</em> the BS/STmin bytes, so those
    /// are never overwritten (fixes review §1.1 point 3).
    /// </summary>
    public static int BuildFlowControl(Span<byte> destination, in IsoTpEndpoint endpoint,
        FlowStatus flowStatus, byte blockSize, byte stMinRaw, bool isCanFd, bool padding,
        byte paddingByte = DefaultPaddingByte)
    {
        int addrExt = endpoint.AddressExtensionSize;
        int maxFrame = isCanFd ? CanFdMaxData : ClassicCanMaxData;

        if (destination.Length < maxFrame)
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length,
                $"Destination buffer must be at least {maxFrame} bytes for the requested frame kind.");

        if (endpoint.UsesAddressExtension)
            destination[0] = endpoint.AddressExtension;

        int pciIndex = addrExt;
        // PCI nibble MUST be FlowControl (0x3), not FirstFrame (0x1) — fixes review §1.1 point 2.
        destination[pciIndex] = (byte)(((byte)PciType.FlowControl << 4) | ((byte)flowStatus & 0x0F));
        destination[pciIndex + 1] = blockSize;
        destination[pciIndex + 2] = stMinRaw;

        int payloadLen = addrExt + 3;
        int frameLen = padding ? NextValidFrameLength(payloadLen, isCanFd) : payloadLen;
        if (frameLen > payloadLen)
            destination.Slice(payloadLen, frameLen - payloadLen).Fill(paddingByte);

        return frameLen;
    }

    // -------------------------------------------------------------------------------------------
    // PCI parser
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Bounds-checked parser for the ISO-TP Protocol-Control-Information of a received CAN frame.
    /// Never throws <see cref="IndexOutOfRangeException"/> — a truncated frame returns
    /// <c>false</c> with <c>pci</c> set to <see langword="default"/> (fixes review §1.1 point 6).
    /// </summary>
    /// <param name="canPayload">Raw CAN data payload (up to 8 bytes for classic CAN, up to 64
    /// bytes for CAN-FD).</param>
    /// <param name="endpoint">Endpoint whose addressing mode decides whether the first payload
    /// byte is the address-extension byte.</param>
    /// <param name="isCanFd">
    /// <c>true</c> when the caller knows the frame was received as a CAN-FD frame, <c>false</c> for
    /// classic CAN. The Single-Frame and First-Frame escape forms (<c>PCI 0x00 LEN …</c> and
    /// <c>PCI 0x10 0x00 LEN[4] …</c>) are only defined on CAN-FD per ISO 15765-2, so this flag is
    /// required to distinguish a legitimate escape header from a malformed classic-CAN PCI whose
    /// SF_DL / FF_DL is zero (fixes review §1.1 point 8 / bugbot 3594958440 / 3594958445).
    /// </param>
    /// <param name="pci">On success, the parsed PCI view.</param>
    /// <returns>
    /// <c>true</c> when the frame has enough bytes and a valid PCI nibble; <c>false</c> for
    /// truncated frames, reserved PCI nibbles (&gt; 3), reserved Flow-Status values (&gt; 2),
    /// escape-form PCIs on classic CAN, or CAN-FD Single-Frame short-form SF_DL values above
    /// <see cref="SingleFrameShortFormMaxDataLength"/> for the endpoint's addressing mode
    /// (those lengths must use the <c>0x00 LEN</c> escape — 8..15 without address extension,
    /// 7..15 with extended/mixed addressing).
    /// </returns>
    public static bool TryParsePci(ReadOnlySpan<byte> canPayload, in IsoTpEndpoint endpoint,
        bool isCanFd, out Pci pci)
    {
        pci = default;
        int addrExt = endpoint.AddressExtensionSize;
        int pciIndex = addrExt;
        if (canPayload.Length <= pciIndex)
            return false;

        byte first = canPayload[pciIndex];
        int typeNibble = first >> 4;
        if (typeNibble > (int)PciType.FlowControl)
            return false; // reserved / unknown PCI type

        var type = (PciType)typeNibble;
        switch (type)
        {
            case PciType.SingleFrame:
                {
                    int shortLen = first & 0x0F;
                    if (shortLen == 0)
                    {
                        // CAN-FD SF escape form: 0x00 LEN, then user data. On classic CAN a zero
                        // SF_DL nibble is reserved/invalid — reject rather than mis-parsing it as
                        // an escape header (fixes bugbot 3594958440).
                        if (!isCanFd)
                            return false;
                        int lenIndex = pciIndex + 1;
                        if (canPayload.Length <= lenIndex)
                            return false;
                        int longLen = canPayload[lenIndex];
                        int dataOffset = pciIndex + 2;
                        if (longLen < 1 || canPayload.Length < dataOffset + longLen)
                            return false;
                        pci = new Pci(type, longLen, 0, FlowStatus.ClearToSend, 0, 0, TimeSpan.Zero, dataOffset);
                        return true;
                    }
                    else
                    {
                        // ISO 15765-2: on CAN-FD the one-byte SF PCI (SF_DL in the low nibble) is
                        // only valid up to SingleFrameShortFormMaxDataLength for the endpoint's
                        // addressing mode (1..7 normal, 1..6 with address extension). Longer
                        // payloads must use the 0x00/LEN escape that BuildSingleFrame already
                        // emits (fixes bugbot 3596033572 / 3596165656). Classic CAN keeps
                        // accepting the low-nibble length when the payload is long enough
                        // (in practice still within the short-form cap inside an 8-byte frame).
                        if (isCanFd &&
                            shortLen > SingleFrameShortFormMaxDataLength(endpoint.UsesAddressExtension))
                            return false;
                        int dataOffset = pciIndex + 1;
                        if (canPayload.Length < dataOffset + shortLen)
                            return false;
                        pci = new Pci(type, shortLen, 0, FlowStatus.ClearToSend, 0, 0, TimeSpan.Zero, dataOffset);
                        return true;
                    }
                }

            case PciType.FirstFrame:
                {
                    int secondIndex = pciIndex + 1;
                    if (canPayload.Length <= secondIndex)
                        return false;
                    int highNibble = first & 0x0F;
                    int lowByte = canPayload[secondIndex];
                    if (highNibble == 0 && lowByte == 0)
                    {
                        // CAN-FD FF escape form: 0x10 0x00 followed by a 4-byte length. On classic
                        // CAN this bit-pattern would announce an FF_DL of zero, which ISO 15765-2
                        // does not permit and which cannot be an escape header either (the escape
                        // form is CAN-FD-only). Reject to avoid conflating the two encodings
                        // (fixes bugbot 3594958445).
                        if (!isCanFd)
                            return false;
                        int lenStart = pciIndex + 2;
                        if (canPayload.Length < lenStart + 4)
                            return false;
                        uint longLen =
                            ((uint)canPayload[lenStart] << 24) |
                            ((uint)canPayload[lenStart + 1] << 16) |
                            ((uint)canPayload[lenStart + 2] << 8) |
                            canPayload[lenStart + 3];
                        if (longLen > int.MaxValue)
                            return false;
                        pci = new Pci(type, (int)longLen, 0, FlowStatus.ClearToSend, 0, 0, TimeSpan.Zero,
                            pciIndex + 6);
                        return true;
                    }
                    else
                    {
                        // Correct parenthesization — fixes review §1.1 point 4:
                        //   (data[pciStart] & 0x0F) << 8 | data[pciStart + 1]
                        int length = ((first & 0x0F) << 8) | lowByte;
                        pci = new Pci(type, length, 0, FlowStatus.ClearToSend, 0, 0, TimeSpan.Zero,
                            pciIndex + 2);
                        return true;
                    }
                }

            case PciType.ConsecutiveFrame:
                {
                    byte sn = (byte)(first & 0x0F);
                    pci = new Pci(type, 0, sn, FlowStatus.ClearToSend, 0, 0, TimeSpan.Zero, pciIndex + 1);
                    return true;
                }

            case PciType.FlowControl:
                {
                    int fsNibble = first & 0x0F;
                    if (fsNibble > (int)FlowStatus.Overflow)
                        return false;
                    int bsIndex = pciIndex + 1;
                    int stIndex = pciIndex + 2;
                    if (canPayload.Length <= stIndex)
                        return false;
                    byte bs = canPayload[bsIndex];
                    byte stRaw = canPayload[stIndex];
                    pci = new Pci(type, 0, 0, (FlowStatus)fsNibble, bs, stRaw, DecodeStMin(stRaw),
                        pciIndex + 3);
                    return true;
                }
        }

        return false;
    }

    // -------------------------------------------------------------------------------------------
    // STmin
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Encodes a <see cref="TimeSpan"/> STmin value as an ISO 15765-2 raw byte.
    /// <list type="bullet">
    ///   <item><description><c>0..127 ms</c> in 1-ms steps → <c>0x00..0x7F</c></description></item>
    ///   <item><description><c>100..900 µs</c> in 100-µs steps → <c>0xF1..0xF9</c></description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The commonly-used values <c>0 ms</c> and <c>1 ms</c> encode as <c>0x00</c> and <c>0x01</c>
    /// respectively (fixes review §1.1 point 5). Values that fall between the two ISO 15765-2
    /// ranges (e.g. 999 µs) or exceed 127 ms are clamped to the nearest representable value.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static byte EncodeStMin(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(value), value, "STmin must be non-negative.");

        // Ticks are 100 ns each -> 10 ticks per microsecond.
        long micro = value.Ticks / 10;
        if (micro <= 0)
            return 0x00;

        if (micro >= 1000)
        {
            long ms = (micro + 500) / 1000; // round-nearest to milliseconds
            if (ms <= 0) return 0x00;
            if (ms >= 127) return 0x7F;
            return (byte)ms;
        }

        // Sub-millisecond band 100..900 µs, granularity 100 µs.
        if (micro < 100)
            return 0xF1; // clamp to smallest sub-ms step
        long step = micro / 100;
        if (step >= 9) return 0xF9;
        return (byte)(0xF0 + step);
    }

    /// <summary>
    /// Decodes an ISO 15765-2 STmin raw byte into a <see cref="TimeSpan"/>. Reserved values
    /// (<c>0x80..0xF0</c> and <c>0xFA..0xFF</c>) are treated as <c>0x7F</c> = 127 ms per the
    /// specification, which fixes review §1.1 point 6 / FR-TP-007 / FR-RAW-052.
    /// </summary>
    public static TimeSpan DecodeStMin(byte raw)
    {
        if (raw <= 0x7F)
            return TimeSpan.FromMilliseconds(raw);
        if (raw >= 0xF1 && raw <= 0xF9)
            return TimeSpan.FromTicks((raw - 0xF0) * 1000L); // (raw-0xF0)*100µs, 1µs = 10 ticks
        // Reserved: 0x80..0xF0 and 0xFA..0xFF -> treat as 0x7F (127 ms).
        return TimeSpan.FromMilliseconds(0x7F);
    }
}
