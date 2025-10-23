using System.Threading.Tasks;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Services
{
    public interface IDeviceService
    {
        Task<DeviceCapabilities> GetCapabilitiesAsync(string endpoint);
    }
}

