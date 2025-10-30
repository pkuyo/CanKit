using CanKit.Core.Utils;

namespace CanKit.Protocol.IsoTp.Defines;

public sealed class IsoTpOptions
{
    public bool UseCanFd { get; init; } = false;
    public bool ClassicCanPadding { get; init; } = true;
    public TimeSpan? GlobalBusGuard { get; init; } = null;   // 数据帧最小全局间隔（FC不受限）

    public QueuedCanBusOptions? QueuedCanBusOptions { get; init; } = null;

    public TimeSpan N_As { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan N_Ar { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan N_Bs { get; init; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan N_Br { get; init; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan N_Cs { get; init; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan N_Cr { get; init; } = TimeSpan.FromMilliseconds(1000);
}
