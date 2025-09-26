using CanKit.Adapter.ZLG.Definitions;
using CanKit.Core.Abstractions;

namespace CanKit.Adapter.ZLG.Transceivers
{
    public interface IZlgTransceiver : ITransceiver
    {
        public ZlgFrameType FrameType { get; }
    }
}
