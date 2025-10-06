using System;

namespace CanKit.Core.Abstractions;

/// <summary>
/// Optional owner attachment for bus lifetime (可选的所有者附加，用于总线生命周期管理)。
/// 总线释放时会一并释放附加的所有者。
/// </summary>
public interface IBusOwnership
{
    /// <summary>
    /// Attach an owner disposed with the bus (附加随总线释放的所有者)。
    /// </summary>
    /// <param name="owner">Owner to dispose with bus (随总线释放的所有者)。</param>
    void AttachOwner(IDisposable owner);
}
