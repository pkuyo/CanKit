using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Core;
using CanKit.Core.Definitions;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Services
{
    public class CanKitDeviceService : IDeviceService
    {
        public Task<DeviceCapabilities> GetCapabilitiesAsync(string endpoint)
        {
            var caps = CanBus.QueryCapabilities(endpoint);
            bool can20 = (caps.Features & CanFeature.CanClassic) != 0;
            bool canFd = (caps.Features & CanFeature.CanFd) != 0;

            // Current version Library does not expose discrete bit tables; provide common presets
            var bitRates = new List<int> { 50_000, 100_000, 125_000, 250_000, 500_000, 1_000_000 };
            var dataBitRates = new List<int> { 500_000, 1_000_000, 2_000_000, 4_000_000, 5_000_000, 8_000_000 };
            return Task.FromResult(new DeviceCapabilities
            {
                SupportsCan20 = can20,
                SupportsCanFd = canFd,
                SupportedBitRates = bitRates,
                SupportedDataBitRates = dataBitRates
            });
        }
    }
}

