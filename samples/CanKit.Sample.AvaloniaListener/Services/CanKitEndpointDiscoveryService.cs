using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CanKit.Core.Endpoints;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Services
{
    public class CanKitEndpointDiscoveryService : IEndpointDiscoveryService
    {
        public Task<IReadOnlyList<EndpointInfo>> DiscoverAsync()
        {
            var list = new List<EndpointInfo>();
            try
            {
                var entries = BusEndpointEntry.Enumerate("pcan", "kvaser", "socketcan", "zlg");
                foreach (var ep in entries)
                {
                    list.Add(new EndpointInfo
                    {
                        Id = ep.Endpoint,
                        DisplayName = string.IsNullOrWhiteSpace(ep.Title) ? ep.Endpoint : ep.Title!,
                        IsCustom = false
                    });
                }
            }
            catch
            {
                // ignore discovery failures; user can still enter custom endpoint
            }

            // Always append Custom option
            list.Add(EndpointInfo.Custom());
            return Task.FromResult<IReadOnlyList<EndpointInfo>>(list);
        }
    }
}

