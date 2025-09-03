using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Registry;
using Pkuyo.CanKit.ZLG.Options;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG
{

    public abstract class ZlgCanProvider : ICanModelProvider
    {
        public abstract DeviceType DeviceType { get; }
        public virtual CanFeature Features => CanFeature.CanClassic | CanFeature.Filters;

        public virtual bool IsFd => false;
        
        public virtual bool EnableMerge => false;
        
        public ICanFactory Factory => CanRegistry.Registry.Factory("Zlg");
        
        public (IDeviceOptions,IDeviceInitOptionsConfigurator) GetDeviceOptions()
        {
            var option = new ZlgDeviceOptions(this);
            var cfg = new ZlgDeviceInitOptionsConfigurator();
            cfg.Init(option, Features);
            return (option, cfg);
        }

        public  (IChannelOptions,IChannelInitOptionsConfigurator) GetChannelOptions(int channelIndex)
        {
            var option = new ZlgChannelOptions(this);
            var cfg = new ZlgChannelInitConfigurator();
            cfg.Init(option, Features);
            return (option, cfg);
        }
    }
}