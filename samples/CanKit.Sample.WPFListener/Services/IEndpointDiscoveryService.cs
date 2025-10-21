using System.Collections.Generic;
using System.Threading.Tasks;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Services
{
    public interface IEndpointDiscoveryService
    {
        Task<IReadOnlyList<EndpointInfo>> DiscoverAsync();
    }
}

