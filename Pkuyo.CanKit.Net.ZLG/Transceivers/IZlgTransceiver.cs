using Pkuyo.CanKit.Net.Core.Abstractions;

namespace Pkuyo.CanKit.ZLG.Transceivers
{
    public interface IZlgTransceiver : ITransceiver
    {
        public ZlgFrameType FrameType { get; }
    }
}