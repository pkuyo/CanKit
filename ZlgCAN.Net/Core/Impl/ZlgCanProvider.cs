using System.Collections.Generic;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;
using ZlgCAN.Net.Core.Impl.Options;
using ZlgCAN.Net.Core.Impl.Transceivers;

namespace ZlgCAN.Net.Core.Impl
{

    public abstract class ZlgCanProvider : ICanModelProvider
    {
        public abstract DeviceType DeviceType { get; }
        public abstract CanFeature Features { get; }

        public virtual bool EnableMerge => false;
        public virtual bool EnableLin => false;
        
        public ICanFactory Factory { get; } = CanCore.Registry.Factory("ZlgCan");
        
        public IDeviceOptions GetDeviceOptions()
        {
            return new ZlgDeviceOptions(this);
        }

        public IChannelOptions GetChannelOptions(int channelIndex)
        {
            return new ZlgChannelOptions(this);
        }

        public IEnumerable<ITransceiver> CreateTransceivers()
        {
            if ((uint)(Features & CanFeature.CanClassic) == 1U)
                yield return new ZlgCanClassicTransceiver();
            
            if ((uint)(Features & CanFeature.CanFd) == 1U)
                yield return new ZlgCanFdTransceiver();

            if (EnableLin)
                yield return new ZlgLinTransceiver();

            if (EnableMerge)
                yield return new ZlgMergeTransceiver();
        }
    }
}