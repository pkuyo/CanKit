using System.Collections.ObjectModel;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class DeviceSessionConnectionOptionsContext : IConnectionOptionsContext
    {
        private readonly DeviceSessionViewModel _session;

        public DeviceSessionConnectionOptionsContext(DeviceSessionViewModel session)
        {
            _session = session;
        }

        public ObservableCollection<int> BitRates => _session.BitRates;
        public ObservableCollection<int> DataBitRates => _session.DataBitRates;
        public ObservableCollection<FilterRuleModel> Filters => _session.Filters;

        public bool SupportsCan20 => _session.SupportsCan20;
        public bool SupportsCanFd => _session.SupportsCanFd;
        public bool SupportsListenOnly => _session.SupportsListenOnly;
        public bool SupportsErrorCounters => _session.SupportsErrorCounters;

        public bool UseCan20
        {
            get => _session.UseCan20;
            set => _session.UseCan20 = value;
        }

        public bool UseCanFd
        {
            get => _session.UseCanFd;
            set => _session.UseCanFd = value;
        }

        public int? SelectedBitRate
        {
            get => _session.SelectedBitRate;
            set => _session.SelectedBitRate = value;
        }

        public int? SelectedDataBitRate
        {
            get => _session.SelectedDataBitRate;
            set => _session.SelectedDataBitRate = value;
        }

        public bool ListenOnly
        {
            get => _session.ListenOnly;
            set => _session.ListenOnly = value;
        }

        public int ErrorCountersPeriodMs
        {
            get => _session.ErrorCountersPeriodMs;
            set => _session.ErrorCountersPeriodMs = value;
        }
    }
}

