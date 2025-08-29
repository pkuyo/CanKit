

using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Impl;

namespace ZlgCAN.Net.Core.Transceivers
{
    public interface IZlgTransceiver : ITransceiver
    {
        public ZlgFrameType FrameType { get; }
    }
}