using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Core.Definitions;

namespace CanKit.Sample.AvaloniaListener.Abstractions
{
    public interface IConnectionService
    {
        Task StartAsync(string endpoint,
            bool can20,
            int bitRate,
            int dataBitRate,
            IReadOnlyList<FilterRuleModel> filters,
            // New options and hooks
            CanFeature features,
            bool listenOnly,
            int countersPollMs,
            Action<ICanFrame, FrameDirection> onFrame,
            Action<string> onMessage,
            Action<CanErrorCounters>? onCountersUpdated,
            Action<float>? onBusUsageUpdated,
            CancellationToken cancellationToken);

        /// <summary>
        /// Transmit a single CAN frame on the currently opened bus.
        /// Returns number of frames accepted by the driver (0 if not sent).
        /// </summary>
        int Transmit(ICanFrame frame);

        /// <summary>
        /// Start periodic transmissions for the provided frames and periods.
        /// Existing periodic tasks are stopped before starting new ones.
        /// </summary>
        void StartPeriodic(IEnumerable<(ICanFrame frame, TimeSpan period)> items);

        /// <summary>
        /// Stop all periodic transmissions started via StartPeriodic.
        /// </summary>
        void StopPeriodic();
    }
}

