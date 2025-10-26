using System.Collections.ObjectModel;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Abstractions
{
    public interface IConnectionOptionsContext
    {
        // Collections
        ObservableCollection<int> BitRates { get; }
        ObservableCollection<int> DataBitRates { get; }
        ObservableCollection<FilterRuleModel> Filters { get; }

        // Capabilities
        bool SupportsCan20 { get; }
        bool SupportsCanFd { get; }
        bool SupportsListenOnly { get; }
        bool SupportsErrorCounters { get; }

        // Selected values
        bool UseCan20 { get; set; }
        bool UseCanFd { get; set; }
        int? SelectedBitRate { get; set; }
        int? SelectedDataBitRate { get; set; }
        bool ListenOnly { get; set; }
        int ErrorCountersPeriodMs { get; set; }
    }
}

