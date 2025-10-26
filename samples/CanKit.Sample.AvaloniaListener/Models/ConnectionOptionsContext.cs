using System.Collections.ObjectModel;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class ConnectionOptionsContext : IConnectionOptionsContext
    {
        // Standalone defaults for design-time or fallback use
        private readonly ObservableCollection<int> _bitRates = new() { 50_000, 100_000, 125_000, 250_000, 500_000, 1_000_000 };
        private readonly ObservableCollection<int> _dataBitRates = new() { 500_000, 1_000_000, 2_000_000, 4_000_000, 5_000_000, 8_000_000 };
        private readonly ObservableCollection<FilterRuleModel> _filters = new();

        public ConnectionOptionsContext(MainViewModel _)
        {
        }

        public ObservableCollection<int> BitRates => _bitRates;
        public ObservableCollection<int> DataBitRates => _dataBitRates;
        public ObservableCollection<FilterRuleModel> Filters => _filters;

        public bool SupportsCan20 => true;
        public bool SupportsCanFd => true;
        public bool SupportsListenOnly => false;
        public bool SupportsErrorCounters => false;

        public bool UseCan20 { get; set; } = true;
        public bool UseCanFd { get; set; } = false;
        public int? SelectedBitRate { get; set; } = 500_000;
        public int? SelectedDataBitRate { get; set; } = 2_000_000;
        public bool ListenOnly { get; set; }
        public int ErrorCountersPeriodMs { get; set; } = 5000;
    }
}
