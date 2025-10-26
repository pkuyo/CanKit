using System.Threading.Tasks;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Abstractions
{
    public interface IDeviceService
    {
        Task<DeviceCapabilities> GetCapabilitiesAsync(string endpoint);
    }
}

