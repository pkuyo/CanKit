using System;

namespace CanKit.Pro.Uds;

/// <summary>
/// Positive-response parameters returned by RequestDownload (0x34, ISO 14229-1 §14.2.2).
/// </summary>
/// <remarks>
/// The ECU echoes a <em>lengthFormatIdentifier</em> byte whose high nibble encodes how many
/// bytes follow to convey <c>maxNumberOfBlockLength</c>. That value is the maximum number of
/// bytes (SID included) the tester may put in a single <c>TransferData</c> request, which the
/// convenience <see cref="IUdsClient.DownloadAsync"/> uses to chunk the payload.
/// </remarks>
public readonly struct UdsDownloadResponse
{
    /// <summary>Raw <c>lengthFormatIdentifier</c> byte echoed by the ECU (byte 1 of the positive
    /// response). The high nibble is the width, in bytes, of the following
    /// <c>maxNumberOfBlockLength</c> field; the low nibble is reserved.</summary>
    public byte LengthFormatIdentifier { get; }

    /// <summary>
    /// Maximum number of bytes (including SID) the ECU is willing to accept in a single
    /// TransferData (0x36) request. Callers MUST NOT exceed this value; the payload chunk in
    /// each TransferData is therefore at most <c>MaxNumberOfBlockLength - 2</c> bytes (SID +
    /// blockSequenceCounter overhead).
    /// </summary>
    public ulong MaxNumberOfBlockLength { get; }

    /// <summary>Creates the response wrapper.</summary>
    public UdsDownloadResponse(byte lengthFormatIdentifier, ulong maxNumberOfBlockLength)
    {
        LengthFormatIdentifier = lengthFormatIdentifier;
        MaxNumberOfBlockLength = maxNumberOfBlockLength;
    }
}

/// <summary>
/// Positive-response parameters returned by RequestUpload (0x35, ISO 14229-1 §14.1.2). Mirrors
/// <see cref="UdsDownloadResponse"/>; the max block length constrains the size of the
/// TransferData responses the ECU will send back to the tester.
/// </summary>
public readonly struct UdsUploadResponse
{
    /// <inheritdoc cref="UdsDownloadResponse.LengthFormatIdentifier"/>
    public byte LengthFormatIdentifier { get; }

    /// <inheritdoc cref="UdsDownloadResponse.MaxNumberOfBlockLength"/>
    public ulong MaxNumberOfBlockLength { get; }

    /// <summary>Creates the response wrapper.</summary>
    public UdsUploadResponse(byte lengthFormatIdentifier, ulong maxNumberOfBlockLength)
    {
        LengthFormatIdentifier = lengthFormatIdentifier;
        MaxNumberOfBlockLength = maxNumberOfBlockLength;
    }
}
