namespace CanKit.Protocol.IsoTp.Defines;

public sealed class RxFcPolicy
{
    public int BS { get; set; } = 8; // 0=无限
    public TimeSpan STmin { get; set; } = TimeSpan.FromMilliseconds(10);
    public bool AllowWT { get; set; } = true;
}
