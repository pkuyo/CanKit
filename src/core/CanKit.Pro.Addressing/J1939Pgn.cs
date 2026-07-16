using System;

namespace CanKit.Pro.Addressing
{
    /// <summary>
    /// Helpers for SAE J1939 Parameter Group Number classification.
    /// </summary>
    public static class J1939Pgn
    {
        /// <summary>Largest valid 18-bit J1939 PGN value.</summary>
        public const uint MaxValue = 0x3FFFF;

        /// <summary>First PDU2 PDU Format value; PF values below this are PDU1.</summary>
        public const byte Pdu2StartFormat = 0xF0;

        /// <summary>J1939 global destination address.</summary>
        public const byte GlobalAddress = 0xFF;

        /// <summary>J1939 null source address used by Cannot Claim Address.</summary>
        public const byte NullAddress = 0xFE;

        /// <summary>Request PGN, 59904 (0xEA00).</summary>
        public const uint Request = 0xEA00;

        /// <summary>Transport Protocol Data Transfer PGN, 60160 (0xEB00).</summary>
        public const uint TpDt = 0xEB00;

        /// <summary>Transport Protocol Connection Management PGN, 60416 (0xEC00).</summary>
        public const uint TpCm = 0xEC00;

        /// <summary>Address Claimed PGN, 60928 (0xEE00).</summary>
        public const uint AddressClaimed = 0xEE00;

        /// <summary>
        /// Cannot Claim Address uses the Address Claimed PGN with source address 0xFE.
        /// </summary>
        public const uint CannotClaim = AddressClaimed;

        /// <summary>TP.CM control byte for Broadcast Announce Message.</summary>
        public const byte TpCmControlBam = 0x20;

        /// <summary>
        /// Returns true when a PDU Format value denotes PDU1, where PS is a destination address.
        /// </summary>
        public static bool IsPdu1(byte pduFormat) => pduFormat < Pdu2StartFormat;

        /// <summary>
        /// Returns true when a PGN denotes PDU1, where the low byte is not a group extension.
        /// </summary>
        public static bool IsPdu1(uint pgn) => IsPdu1(GetPduFormat(pgn));

        /// <summary>
        /// Returns true when a PDU Format value denotes PDU2, where PS is a group extension.
        /// </summary>
        public static bool IsPdu2(byte pduFormat) => pduFormat >= Pdu2StartFormat;

        /// <summary>
        /// Returns true when a PGN denotes PDU2, where the low byte is a group extension.
        /// </summary>
        public static bool IsPdu2(uint pgn) => IsPdu2(GetPduFormat(pgn));

        /// <summary>
        /// Extracts the PDU Format byte from a PGN.
        /// </summary>
        public static byte GetPduFormat(uint pgn)
        {
            ValidatePgn(pgn);
            return (byte)((pgn >> 8) & 0xFF);
        }

        /// <summary>
        /// Extracts the PDU2 group extension byte from a PGN, or 0 for PDU1 PGNs.
        /// </summary>
        public static byte GetGroupExtension(uint pgn)
        {
            ValidatePgn(pgn);
            return IsPdu2(GetPduFormat(pgn)) ? (byte)(pgn & 0xFF) : (byte)0;
        }

        /// <summary>
        /// Tries to extract a PDU2 group extension byte from a PGN.
        /// </summary>
        /// <returns>True for PDU2 PGNs; false for PDU1 PGNs.</returns>
        public static bool TryGetGroupExtension(uint pgn, out byte groupExtension)
        {
            ValidatePgn(pgn);
            if (IsPdu1(GetPduFormat(pgn)))
            {
                groupExtension = 0;
                return false;
            }

            groupExtension = (byte)(pgn & 0xFF);
            return true;
        }

        /// <summary>
        /// Normalizes a PGN by clearing the low byte for PDU1 values.
        /// </summary>
        public static uint Normalize(uint pgn)
        {
            ValidatePgn(pgn);
            return IsPdu1(GetPduFormat(pgn)) ? pgn & 0x3FF00u : pgn;
        }

        /// <summary>Returns true when the PGN is Request (0xEA00).</summary>
        public static bool IsRequest(uint pgn) => Normalize(pgn) == Request;

        /// <summary>Returns true when the PGN is TP.CM (0xEC00).</summary>
        public static bool IsTransportCm(uint pgn) => Normalize(pgn) == TpCm;

        /// <summary>Returns true when the PGN is TP.DT (0xEB00).</summary>
        public static bool IsTransportDt(uint pgn) => Normalize(pgn) == TpDt;

        /// <summary>Returns true when the PGN is Address Claimed (0xEE00).</summary>
        public static bool IsAddressClaim(uint pgn) => Normalize(pgn) == AddressClaimed;

        /// <summary>
        /// Returns true when decomposed fields represent Cannot Claim Address.
        /// </summary>
        public static bool IsCannotClaim(J1939Fields fields) =>
            IsAddressClaim(fields.Pgn)
            && fields.SourceAddress == NullAddress
            && fields.DestinationAddress == GlobalAddress;

        /// <summary>
        /// Returns true when a TP.CM control byte is Broadcast Announce Message.
        /// </summary>
        public static bool IsBamControlByte(byte controlByte) => controlByte == TpCmControlBam;

        /// <summary>
        /// Returns true when a frame is TP.CM and its first payload byte is the BAM control byte.
        /// </summary>
        public static bool IsBam(uint pgn, byte tpCmControlByte) =>
            IsTransportCm(pgn) && IsBamControlByte(tpCmControlByte);

        private static void ValidatePgn(uint pgn)
        {
            if (pgn > MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pgn), pgn, "PGN must fit in 18 bits (Reserved|DataPage|PF|GE).");
        }
    }
}
