using System;
using System.Collections.Generic;
using CanKit.Core.Definitions;

namespace CanKit.Sample.AvaloniaListener.Services
{
    public interface IPeriodicTxService
    {
        void Start(IEnumerable<(ICanFrame frame, TimeSpan period)> items);
        void Stop();
    }
}

