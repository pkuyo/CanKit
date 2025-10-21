using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Services
{
    public interface IListenerService
    {
        Task StartAsync(string endpoint,
            bool can20,
            int bitRate,
            int dataBitRate,
            IReadOnlyList<FilterRuleModel> filters,
            Action<string> onMessage,
            CancellationToken cancellationToken);
    }
}
