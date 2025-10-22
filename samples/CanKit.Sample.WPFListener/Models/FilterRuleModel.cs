using CanKit.Core.Definitions;

namespace EndpointListenerWpf.Models
{
    public enum FilterKind
    {
        Mask,
        Range
    }

    public class FilterRuleModel
    {
        public FilterKind Kind { get; set; }
        public CanFilterIDType IdType { get; set; } = CanFilterIDType.Standard;

        // For Mask
        public int AccCode { get; set; }
        public int AccMask { get; set; }

        // For Range
        public int From
        {
            get => AccCode;
            set => AccCode = value;
        }

        public int To
        {
            get => AccMask;
            set => AccMask = value;
        }

        public override string ToString()
        {
            return Kind switch
            {
                FilterKind.Mask => $"Mask: acc=0x{AccCode:X8}, mask=0x{AccMask:X8}, {IdType}",
                FilterKind.Range => $"Range: 0x{From:X}..0x{To:X}, {IdType}",
                _ => base.ToString() ?? ""
            };
        }
    }
}

