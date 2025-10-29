using CanKit.Core.Definitions;

namespace CanKit.Protocol.IsoTp.Core;

internal sealed class Router
{
    private readonly List<IsoTpChannelCore> _channels = new();

    public void Register(IsoTpChannelCore ch) => _channels.Add(ch);
    public void Unregister(IsoTpChannelCore ch) => _channels.Remove(ch);

    public bool Route(in CanReceiveData rx)
    {
        // TODO: 按 RxId/IDE（以及扩展/混合寻址时 N_AI 首字节）找到归属通道
        foreach (var ch in _channels)
            if (ch.Match(rx)) { ch.OnRx(rx); return true; }
        return false;
    }
}
