using System;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Abstractions.API.Transport.Definitions;

public class IsoTpOptions
{
    public bool CanPadding { get; set; }
    public TimeSpan? GlobalBusGuard { get; set; }

    public CanProtocolMode Protocol { get; set; }
    public TimeSpan N_As { get; set; }
    public TimeSpan N_Ar { get; set; }
    public TimeSpan N_Bs { get; set; }
    public TimeSpan N_Br { get; set; }
    public TimeSpan N_Cs { get; set; }
    public TimeSpan N_Cr { get; set; }

    public IsoTpEndpoint Endpoint { get; set; } = new();
}
