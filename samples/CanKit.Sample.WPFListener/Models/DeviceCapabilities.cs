using System.Collections.Generic;

namespace EndpointListenerWpf.Models
{
    public class DeviceCapabilities
    {
        public bool SupportsCan20 { get; set; }
        public bool SupportsCanFd { get; set; }
        public List<int> SupportedBitRates { get; set; } = new List<int>();

        public List<int> SupportedDataBitRates { get; set; } = new List<int>();
    }
}

