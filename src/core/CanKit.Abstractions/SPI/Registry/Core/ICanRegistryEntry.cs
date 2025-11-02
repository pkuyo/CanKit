using CanKit.Core.Registry;

namespace CanKit.Abstractions.SPI.Registry.Core;

/// <summary>
/// A registry entry that executes registration logic against a discovered ICanRegister.
/// 用于执行注册逻辑的入口类型（接收一个 ICanRegister）。
/// </summary>
public interface ICanRegistryEntry
{
    /// <summary>
    /// Perform registration using the provided register and registry instance.
    /// 使用给定的注册器与注册表执行注册逻辑。
    /// </summary>
    void Register(string name, ICanRegister register);
}

