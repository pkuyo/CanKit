using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Transport.IsoTp.Core;

internal sealed class Router
{

    private readonly List<IsoTpChannelCore> _channels = new();

    public void Register(IsoTpChannelCore ch) => _channels.Add(ch);
    public void Unregister(IsoTpChannelCore ch) => _channels.Remove(ch);

    public bool Route(in CanReceiveData rx)
    {
        foreach (var ch in _channels)
        {
            if (ch.Match(rx))
            {
                ch.OnRx(rx);
                return true;
            }
        }

        return false;
    }

    public bool Route(OutboundItem item)
    {
        foreach (var ch in _channels)
        {
            if (ch.Match(item.Endpoint))
            {
                ch.OnFrameAccepted(item);
                return true;
            }
        }

        return false;
    }

    public bool Route(OutboundItem item, Exception exception)
    {
        foreach (var ch in _channels)
        {
            if (ch.Match(item.Endpoint))
            {
                ch.OnFrameSendFailed(item, exception);
                return true;
            }
        }

        return false;
    }

}
