using CanKit.Core.Definitions;

namespace CanKit.Protocol.IsoTp.Core;

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

    public bool Route(IsoTpChannelCore.TxOperation tx, in IsoTpChannelCore.TxFrame frame)
    {
        foreach (var ch in _channels)
        {
            if (ch.Match(tx))
            {
                ch.OnTx(tx, frame);
                return true;
            }
        }

        return false;
    }

    public bool Route(in IsoTpChannelCore.TxOperation tx, in IsoTpChannelCore.TxFrame frame, Exception exception)
    {
        foreach (var ch in _channels)
        {
            if (ch.Match(tx))
            {
                ch.OnTxFailed(tx, frame, exception);
                return true;
            }
        }

        return false;
    }
}
