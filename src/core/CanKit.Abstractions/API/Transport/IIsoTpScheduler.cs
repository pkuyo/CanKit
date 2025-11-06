using System;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.API.Transport.Definitions;

namespace CanKit.Abstractions.API.Transport;

public interface IIsoTpScheduler : IDisposable
{

    IBusRTOptionsConfigurator Options { get; }

    BusNativeHandle NativeHandle { get; }

    void AddChannel(IIsoTpChannel channel);

    void RemoveChannel(IIsoTpChannel channel);
}
