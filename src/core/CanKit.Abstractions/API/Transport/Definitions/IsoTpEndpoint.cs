namespace CanKit.Abstractions.API.Transport.Definitions;

public enum AddressingFormat
{
    Normal,
    NormalFixed,
    Extended,
    Mixed
}

public enum TargetType
{
    Physical,
    Functional
}

public sealed class IsoTpEndpoint
{
    public int TxId { get; private init; }
    public int RxId { get; private init; }
    public bool IsExtendedId { get; private init; }
    public AddressingFormat AddressingFormat { get; private init; } = AddressingFormat.Normal;

    public TargetType TargetType { get; private init; } = TargetType.Physical;

    public byte? SourceAddress { get; private init; }
    public byte? TargetAddress { get; private init; }

    public byte? ExtendedAddress { get; init; }

    public bool UsePayload => AddressingFormat is AddressingFormat.Extended or AddressingFormat.Mixed;

    private const int CAN_SFF_MASK = 0x7FF;
    private const int CAN_EFF_MASK = 0x1FFFFFFF;


    /// <summary>
    /// 发送方向：返回 (带扩展位的 CAN ID, 负载第0字节的地址值 或 null)。
    /// Extended: addrByte=TargetAddress；Mixed: addrByte=ExtendedAddress；Normal/Fixed: null。
    /// </summary>
    public (int idWithFlag, byte? addrByte) GetTxId()
    {
        int id = IsExtendedId
            ? (TxId & CAN_EFF_MASK)
            : (TxId & CAN_SFF_MASK);

        byte? addr = AddressingFormat switch
        {
            AddressingFormat.Extended => TargetAddress,
            AddressingFormat.Mixed => ExtendedAddress,
            _ => null
        };
        return (id, addr);
    }

    /// <summary>
    /// 接收方向：返回 (带扩展位的 CAN ID, 负载第0字节的地址值 或 null)。
    /// Extended: addrByte=SourceAddress；Mixed: addrByte=ExtendedAddress；Normal/Fixed: null。
    /// </summary>
    public (int idWithFlag, byte? addrByte) GetRxId()
    {
        int id = IsExtendedId
            ? (RxId & CAN_EFF_MASK) : (RxId & CAN_SFF_MASK);

        byte? addr = AddressingFormat switch
        {
            AddressingFormat.Extended => SourceAddress,
            AddressingFormat.Mixed => ExtendedAddress,
            _ => null
        };
        return (id, addr);
    }

    public static IsoTpEndpoint CreateNormal(int txId, int rxId, bool isExtend = false,
        TargetType type = TargetType.Physical)
        => new IsoTpEndpoint
        {
            TxId = txId & (isExtend ? CAN_EFF_MASK : CAN_SFF_MASK),
            RxId = rxId & (isExtend ? CAN_EFF_MASK : CAN_SFF_MASK),
            IsExtendedId = isExtend,
            AddressingFormat = AddressingFormat.Normal,
            TargetType = type
        };

    public static IsoTpEndpoint CreateNormalFixed(byte sourceAddress, byte targetAddress,
        TargetType type = TargetType.Physical,
        byte prio = 0x18, byte pf = 0xDA)
    {
        if (type == TargetType.Functional)
        {
            var tx = Build29(prio, pf, targetAddress, sourceAddress);
            return new IsoTpEndpoint
            {
                TxId = tx,
                RxId = 0,
                IsExtendedId = true,
                AddressingFormat = AddressingFormat.NormalFixed,
                TargetType = TargetType.Functional,
                SourceAddress = sourceAddress,
                TargetAddress = targetAddress
            };
        }
        else
        {
            var tx = Build29(prio, pf, targetAddress, sourceAddress);
            var rx = Build29(prio, pf, sourceAddress, targetAddress);
            return new IsoTpEndpoint
            {
                TxId = tx,
                RxId = rx,
                IsExtendedId = true,
                AddressingFormat = AddressingFormat.NormalFixed,
                TargetType = TargetType.Physical,
                SourceAddress = sourceAddress,
                TargetAddress = targetAddress
            };
        }
    }

    public static IsoTpEndpoint CreateExtended(int txId, int rxId, bool isExtend, byte sourceAddress,
        byte targetAddress, TargetType type = TargetType.Physical)
        => new IsoTpEndpoint
        {
            TxId = isExtend ? (txId & CAN_EFF_MASK) : (txId & CAN_SFF_MASK),
            RxId = isExtend ? (rxId & CAN_EFF_MASK) : (rxId & CAN_SFF_MASK),
            IsExtendedId = isExtend,
            AddressingFormat = AddressingFormat.Extended,
            TargetType = type,
            SourceAddress = sourceAddress,
            TargetAddress = targetAddress
        };

    public static IsoTpEndpoint CreateMixed(int txId, int rxId, byte extendedAddress, bool isExtend = false,
        TargetType type = TargetType.Physical)
        => new IsoTpEndpoint
        {
            TxId = txId & (isExtend ? CAN_EFF_MASK : CAN_SFF_MASK),
            RxId = rxId & (isExtend ? CAN_EFF_MASK : CAN_SFF_MASK),
            IsExtendedId = isExtend,
            AddressingFormat = AddressingFormat.Mixed,
            TargetType = type,
            ExtendedAddress = extendedAddress
        };

    private static int Build29(byte priority, byte pf, byte ps, byte sa)
    {
        int id = ((priority << 24) | (pf << 16) | (ps << 8) | sa);
        return (id & CAN_EFF_MASK);
    }
}
