using System;

namespace CanKit.Pro.Addressing
{
    /// <summary>
    /// Represents the 64-bit SAE J1939 NAME used by address claiming.
    /// </summary>
    /// <remarks>
    /// Bit layout from least-significant to most-significant bit:
    /// IdentityNumber(21) | ManufacturerCode(11) | EcuInstance(3) |
    /// FunctionInstance(5) | Function(8) | Reserved(1) | VehicleSystem(7) |
    /// VehicleSystemInstance(4) | IndustryGroup(3) | ArbitraryAddressCapable(1).
    /// </remarks>
    public readonly struct J1939Name : IComparable<J1939Name>, IEquatable<J1939Name>
    {
        /// <summary>Largest valid identity number value.</summary>
        public const uint IdentityNumberMax = 0x1FFFFF;

        /// <summary>Largest valid manufacturer code value.</summary>
        public const ushort ManufacturerCodeMax = 0x7FF;

        /// <summary>Largest valid ECU instance value.</summary>
        public const byte EcuInstanceMax = 0x7;

        /// <summary>Largest valid function instance value.</summary>
        public const byte FunctionInstanceMax = 0x1F;

        /// <summary>Largest valid vehicle system value.</summary>
        public const byte VehicleSystemMax = 0x7F;

        /// <summary>Largest valid vehicle system instance value.</summary>
        public const byte VehicleSystemInstanceMax = 0xF;

        /// <summary>Largest valid industry group value.</summary>
        public const byte IndustryGroupMax = 0x7;

        /// <summary>
        /// Initializes a NAME from individual SAE J1939 fields.
        /// </summary>
        public J1939Name(
            uint identityNumber,
            ushort manufacturerCode,
            byte ecuInstance,
            byte functionInstance,
            byte function,
            bool reserved,
            byte vehicleSystem,
            byte vehicleSystemInstance,
            byte industryGroup,
            bool arbitraryAddressCapable)
        {
            Value = Compose(
                identityNumber,
                manufacturerCode,
                ecuInstance,
                functionInstance,
                function,
                reserved,
                vehicleSystem,
                vehicleSystemInstance,
                industryGroup,
                arbitraryAddressCapable);
        }

        private J1939Name(ulong value)
        {
            Value = value;
        }

        /// <summary>Raw 64-bit NAME value.</summary>
        public ulong Value { get; }

        /// <summary>Identity Number field, bits 0-20.</summary>
        public uint IdentityNumber => (uint)(Value & IdentityNumberMax);

        /// <summary>Manufacturer Code field, bits 21-31.</summary>
        public ushort ManufacturerCode => (ushort)((Value >> 21) & ManufacturerCodeMax);

        /// <summary>ECU Instance field, bits 32-34.</summary>
        public byte EcuInstance => (byte)((Value >> 32) & EcuInstanceMax);

        /// <summary>Function Instance field, bits 35-39.</summary>
        public byte FunctionInstance => (byte)((Value >> 35) & FunctionInstanceMax);

        /// <summary>Function field, bits 40-47.</summary>
        public byte Function => (byte)((Value >> 40) & 0xFF);

        /// <summary>Reserved field, bit 48.</summary>
        public bool Reserved => ((Value >> 48) & 0x1) != 0;

        /// <summary>Vehicle System field, bits 49-55.</summary>
        public byte VehicleSystem => (byte)((Value >> 49) & VehicleSystemMax);

        /// <summary>Vehicle System Instance field, bits 56-59.</summary>
        public byte VehicleSystemInstance => (byte)((Value >> 56) & VehicleSystemInstanceMax);

        /// <summary>Industry Group field, bits 60-62.</summary>
        public byte IndustryGroup => (byte)((Value >> 60) & IndustryGroupMax);

        /// <summary>Arbitrary Address Capable field, bit 63.</summary>
        public bool ArbitraryAddressCapable => ((Value >> 63) & 0x1) != 0;

        /// <summary>
        /// Composes a raw 64-bit NAME value from individual SAE J1939 fields.
        /// </summary>
        public static ulong Compose(
            uint identityNumber,
            ushort manufacturerCode,
            byte ecuInstance,
            byte functionInstance,
            byte function,
            bool reserved,
            byte vehicleSystem,
            byte vehicleSystemInstance,
            byte industryGroup,
            bool arbitraryAddressCapable)
        {
            if (identityNumber > IdentityNumberMax)
                throw new ArgumentOutOfRangeException(nameof(identityNumber), identityNumber, "Identity Number must fit in 21 bits.");
            if (manufacturerCode > ManufacturerCodeMax)
                throw new ArgumentOutOfRangeException(nameof(manufacturerCode), manufacturerCode, "Manufacturer Code must fit in 11 bits.");
            if (ecuInstance > EcuInstanceMax)
                throw new ArgumentOutOfRangeException(nameof(ecuInstance), ecuInstance, "ECU Instance must fit in 3 bits.");
            if (functionInstance > FunctionInstanceMax)
                throw new ArgumentOutOfRangeException(nameof(functionInstance), functionInstance, "Function Instance must fit in 5 bits.");
            if (vehicleSystem > VehicleSystemMax)
                throw new ArgumentOutOfRangeException(nameof(vehicleSystem), vehicleSystem, "Vehicle System must fit in 7 bits.");
            if (vehicleSystemInstance > VehicleSystemInstanceMax)
                throw new ArgumentOutOfRangeException(nameof(vehicleSystemInstance), vehicleSystemInstance, "Vehicle System Instance must fit in 4 bits.");
            if (industryGroup > IndustryGroupMax)
                throw new ArgumentOutOfRangeException(nameof(industryGroup), industryGroup, "Industry Group must fit in 3 bits.");

            return identityNumber
                   | ((ulong)manufacturerCode << 21)
                   | ((ulong)ecuInstance << 32)
                   | ((ulong)functionInstance << 35)
                   | ((ulong)function << 40)
                   | ((reserved ? 1UL : 0UL) << 48)
                   | ((ulong)vehicleSystem << 49)
                   | ((ulong)vehicleSystemInstance << 56)
                   | ((ulong)industryGroup << 60)
                   | ((arbitraryAddressCapable ? 1UL : 0UL) << 63);
        }

        /// <summary>
        /// Decomposes a raw 64-bit NAME value into field accessors.
        /// </summary>
        public static J1939Name Decompose(ulong value) => new J1939Name(value);

        /// <summary>
        /// Compares two NAMEs for SAE J1939-81 address claiming priority.
        /// </summary>
        /// <returns>
        /// A negative value when <paramref name="left"/> has higher claim priority and wins
        /// because its unsigned 64-bit NAME is numerically lower; zero when both NAMEs are
        /// identical; a positive value when <paramref name="right"/> wins.
        /// </returns>
        public static int CompareClaimPriority(J1939Name left, J1939Name right) => left.Value.CompareTo(right.Value);

        /// <summary>
        /// Returns true when this NAME wins address claiming arbitration against another NAME.
        /// </summary>
        public bool HasHigherClaimPriorityThan(J1939Name other) => CompareClaimPriority(this, other) < 0;

        /// <summary>
        /// Returns true when this NAME loses address claiming arbitration against another NAME.
        /// </summary>
        public bool HasLowerClaimPriorityThan(J1939Name other) => CompareClaimPriority(this, other) > 0;

        /// <summary>
        /// Compares NAMEs by their raw numeric value, which is also J1939-81 claim priority order.
        /// </summary>
        public int CompareTo(J1939Name other) => Value.CompareTo(other.Value);

        /// <summary>Returns true when both NAME values are identical.</summary>
        public bool Equals(J1939Name other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is J1939Name other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => unchecked((int)Value ^ (int)(Value >> 32));

        /// <inheritdoc />
        public override string ToString() => $"0x{Value:X16}";

        /// <summary>Returns true when both NAME values are identical.</summary>
        public static bool operator ==(J1939Name left, J1939Name right) => left.Equals(right);

        /// <summary>Returns true when the NAME values differ.</summary>
        public static bool operator !=(J1939Name left, J1939Name right) => !left.Equals(right);

        /// <summary>Returns true when the left NAME has higher claim priority than the right NAME.</summary>
        public static bool operator <(J1939Name left, J1939Name right) => left.CompareTo(right) < 0;

        /// <summary>Returns true when the left NAME has lower claim priority than the right NAME.</summary>
        public static bool operator >(J1939Name left, J1939Name right) => left.CompareTo(right) > 0;

        /// <summary>Returns true when the left NAME is equal to or has higher claim priority than the right NAME.</summary>
        public static bool operator <=(J1939Name left, J1939Name right) => left.CompareTo(right) <= 0;

        /// <summary>Returns true when the left NAME is equal to or has lower claim priority than the right NAME.</summary>
        public static bool operator >=(J1939Name left, J1939Name right) => left.CompareTo(right) >= 0;
    }
}
