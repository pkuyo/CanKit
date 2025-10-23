using CanKit.Core.Definitions;

namespace CanKit.Sample.AvaloniaListener.Models
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
        public int From { get; set; }
        public int To { get; set; }

        public override string ToString()
        {
            return Kind switch
            {
                FilterKind.Mask => $"Mask: acc=0x{AccCode:X8}, mask=0x{AccMask:X8}, {IdType}",
                FilterKind.Range => $"Range: {From}..{To}, {IdType}",
                _ => base.ToString() ?? ""
            };
        }
    }
}

