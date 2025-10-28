namespace CanKit.Protocol.IsoTp.Defines;

public enum Addressing { Normal, NormalFixed, Functional, Extended, Mixed,}

public sealed class IsoTpEndpoint
{
    public int TxId { get; init; }
    public int RxId { get; init; }
    public bool IsExtendedId { get; init; }
    public Addressing Addressing { get; init; } = Addressing.Normal;

    public bool IsExtendedAddress => Addressing is Addressing.Mixed or Addressing.Extended;

    public byte? TargetAddress { get; init; }
    public byte? SourceAddress { get; init; }

    public override string ToString() =>
        $"{(IsExtendedId ? "29" : "11")}bit {Addressing} Tx=0x{TxId:X} Rx=0x{RxId:X}";
}
