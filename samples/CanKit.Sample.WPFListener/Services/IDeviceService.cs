using System.Threading.Tasks;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Services
{
    public interface IDeviceService
    {
        Task<DeviceCapabilities> GetCapabilitiesAsync(string endpoint);
    }
}

