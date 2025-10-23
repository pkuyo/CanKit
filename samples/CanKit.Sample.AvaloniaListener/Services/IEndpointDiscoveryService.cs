using System.Collections.Generic;
using System.Threading.Tasks;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Services
{
    public interface IEndpointDiscoveryService
    {
        Task<IReadOnlyList<EndpointInfo>> DiscoverAsync();
    }
}

