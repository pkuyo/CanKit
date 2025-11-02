using CanKit.Abstractions.SPI.Factories;
using CanKit.Core.Registry;

namespace CanKit.Abstractions.SPI.Registry.Core;

public interface ICanRegisterFactory : ICanRegister
{
    (string FactoryId, ICanFactory Factory) Factory { get; }
}

