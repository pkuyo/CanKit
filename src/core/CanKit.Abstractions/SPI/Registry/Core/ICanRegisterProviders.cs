using System.Collections.Generic;
using CanKit.Abstractions.SPI.Providers;
using CanKit.Abstractions.SPI.Registry.Core;

namespace CanKit.Core.Registry;

public interface ICanRegisterProviders : ICanRegister
{
    IEnumerable<ICanModelProvider> Providers { get; }
}

