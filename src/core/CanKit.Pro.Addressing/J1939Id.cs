using System;

namespace CanKit.Pro.Addressing
{
    /// <summary>
    /// Composes and decomposes 29-bit J1939 (SAE J1939-21) CAN identifiers (arc42 "Adressierungs-
    /// Helfer"; SRS FR-RAW-040). This generalizes the single hard-coded-PGN 29-bit ID builder
    /// previously scattered inside <c>IsoTpEndpoint.CreateNormalFixed</c> into a reusable,
    /// general-purpose PGN/Priority/Source-Address helper any protocol layer can call directly.
    /// (29 位 J1939 CAN 标识符的组合与分解。将此前散落于 <c>IsoTpEndpoint.CreateNormalFixed</c> 内、仅支持单一
    /// 固定 PGN 的 29 位 ID 构造逻辑，泛化为可被任意协议层直接调用的通用 PGN/优先级/源地址辅助函数。)
    /// </summary>
    public static class J1939Id
    {
        /// <summary>
        /// Decomposes a 29-bit CAN ID into its J1939 fields (Priority, Reserved, DataPage,
        /// PDU-Format, PDU-Specific, Source-Address, and the derived PGN/destination-address).
        /// (将 29 位 CAN ID 分解为其 J1939 字段。)
        /// </summary>
        /// <param name="canId">
        /// The 29-bit extended CAN ID (flag bits, if any, must already be stripped -- pass
        /// <c>CanFrame.ID</c>/<c>CanFrameView.ID</c> as-is, they are already flag-stripped).
        /// (29 位扩展 CAN ID（若含标志位需已剥离——<c>CanFrame.ID</c>/<c>CanFrameView.ID</c> 已剥离，可直接传入）。)
        /// </param>
        public static J1939Fields Decompose(uint canId)
        {
            CanIdRange.ValidateExtended(canId);
            var priority = (byte)((canId >> 26) & 0x7);
            var reserved = ((canId >> 25) & 0x1) != 0;
            var dataPage = (byte)((canId >> 24) & 0x1);
            var pduFormat = (byte)((canId >> 16) & 0xFF);
            var pduSpecific = (byte)((canId >> 8) & 0xFF);
            var sourceAddress = (byte)(canId & 0xFF);
            return new J1939Fields(priority, reserved, dataPage, pduFormat, pduSpecific, sourceAddress);
        }

        /// <summary>
        /// Composes a 29-bit CAN ID from its raw J1939 fields. (由 J1939 原始字段组合出 29 位 CAN ID。)
        /// </summary>
        /// <param name="priority">Message priority, 0 (highest) – 7 (lowest). Only the low 3 bits are used.</param>
        /// <param name="reserved">Reserved bit (bit 25); pass false unless a specific application defines otherwise.</param>
        /// <param name="dataPage">Data Page bit (bit 24); only the low bit is used.</param>
        /// <param name="pduFormat">PDU Format (PF), bits 23-16.</param>
        /// <param name="pduSpecific">PDU Specific (PS), bits 15-8: destination address (PF &lt; 240) or Group Extension (PF &gt;= 240).</param>
        /// <param name="sourceAddress">Source Address (SA), bits 7-0.</param>
        public static uint Compose(byte priority, bool reserved, byte dataPage, byte pduFormat, byte pduSpecific, byte sourceAddress)
        {
            if (priority > 7) throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be in [0, 7].");
            if (dataPage > 1) throw new ArgumentOutOfRangeException(nameof(dataPage), dataPage, "DataPage must be 0 or 1.");

            var id = ((uint)priority << 26)
                     | ((reserved ? 1u : 0u) << 25)
                     | ((uint)dataPage << 24)
                     | ((uint)pduFormat << 16)
                     | ((uint)pduSpecific << 8)
                     | sourceAddress;
            return CanIdRange.ValidateExtended(id);
        }

        /// <summary>
        /// Composes a 29-bit CAN ID from a PGN, the way protocol code usually thinks about it: "I
        /// want to send this PGN, at this priority, from this source, to this destination."
        /// (以协议代码通常的思考方式——“以此优先级、从此源地址向此目标地址发送此 PGN”——组合出 29 位 CAN ID。)
        /// </summary>
        /// <param name="priority">Message priority, 0 (highest) – 7 (lowest).</param>
        /// <param name="pgn">
        /// Parameter Group Number, as returned by <see cref="J1939Fields.Pgn"/> (up to 18 bits:
        /// Reserved&lt;&lt;17 | DataPage&lt;&lt;16 | PduFormat&lt;&lt;8 | GroupExtension).
        /// </param>
        /// <param name="sourceAddress">Source Address (SA), bits 7-0.</param>
        /// <param name="destinationAddress">
        /// Destination address for a PDU1 (peer-to-peer) PGN -- i.e. when the PGN's PDU Format
        /// byte is &lt; 240. Ignored for a PDU2 (broadcast-only) PGN, since PDU2 messages have no
        /// destination address (defaults to the conventional global/broadcast address 0xFF, which
        /// is simply unused in that case).
        /// (PDU1（点对点）PGN 的目标地址，即当 PGN 的 PDU 格式字节 &lt; 240 时使用；对 PDU2（仅广播）PGN 忽略此参数，
        /// 因其没有目标地址（默认为惯例上的全局/广播地址 0xFF，此时该参数实际未被使用）。)
        /// </param>
        public static uint ComposePgn(byte priority, uint pgn, byte sourceAddress, byte destinationAddress = 0xFF)
        {
            if (pgn > 0x3FFFF) throw new ArgumentOutOfRangeException(nameof(pgn), pgn, "PGN must fit in 18 bits (Reserved|DataPage|PF|GE).");

            var reserved = ((pgn >> 17) & 0x1) != 0;
            var dataPage = (byte)((pgn >> 16) & 0x1);
            var pduFormat = (byte)((pgn >> 8) & 0xFF);
            var pduSpecific = pduFormat < 240 ? destinationAddress : (byte)(pgn & 0xFF);
            return Compose(priority, reserved, dataPage, pduFormat, pduSpecific, sourceAddress);
        }
    }
}
