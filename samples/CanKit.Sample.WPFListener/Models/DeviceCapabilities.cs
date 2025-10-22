using System.Collections.Generic;
using CanKit.Core.Definitions;

namespace EndpointListenerWpf.Models
{
    public class DeviceCapabilities
    {
        public bool SupportsCan20 { get; set; }
        public bool SupportsCanFd { get; set; }
        public List<int> SupportedBitRates { get; set; } = new List<int>();

        public List<int> SupportedDataBitRates { get; set; } = new List<int>();

        // Full feature mask from provider
        public CanFeature Features { get; set; }

        // Convenience flags derived from Features
        public bool SupportsListenOnly => (Features & CanFeature.ListenOnly) != 0;
        public bool SupportsErrorCounters => (Features & CanFeature.ErrorCounters) != 0;
        public bool SupportsErrorFrames => (Features & CanFeature.ErrorFrame) != 0;
        public bool SupportsBusUsage => (Features & CanFeature.BusUsage) != 0;
    }
}
