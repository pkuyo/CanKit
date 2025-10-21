using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EndpointListenerWpf.Models;
using CanKit.Core.Definitions;

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

        /// <summary>
        /// Transmit a single CAN frame on the currently opened bus.
        /// Returns number of frames accepted by the driver (0 if not sent).
        /// </summary>
        int Transmit(ICanFrame frame, int timeOut = 0);
    }
}
