namespace CanKit.Protocol.IsoTp.Defines;


public enum PciType : byte { SF = 0x0, FF = 0x1, CF = 0x2, FC = 0x3 }
public enum FlowStatus : byte { CTS = 0x0, WT = 0x1, OVFLW = 0x2 }

public static class IsoTpConst
{
    public const int ClassicCanMaxData = 8;
    public const int CanFdMaxData = 64;
    public const int SN_Mod = 16;
}
