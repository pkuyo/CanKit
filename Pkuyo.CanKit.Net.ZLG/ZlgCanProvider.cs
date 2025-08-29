using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.ZLG.Options;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG
{

    public abstract class ZlgCanProvider : ICanModelProvider
    {
        public abstract DeviceType DeviceType { get; }
        public virtual CanFeature Features => CanFeature.CanClassic | CanFeature.Filters;

        protected virtual bool EnableMerge => false;
        protected virtual bool EnableLin => false;
        
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