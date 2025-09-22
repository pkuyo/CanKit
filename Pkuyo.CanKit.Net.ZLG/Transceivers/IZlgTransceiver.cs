using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.ZLG.Options;

namespace Pkuyo.CanKit.ZLG.Transceivers
{
    public interface IZlgTransceiver : ITransceiver
    {
        public ZlgFrameType FrameType { get; }
    }
}
