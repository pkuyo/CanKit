using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Registry;
using Pkuyo.CanKit.ZLG.Definitions;
using Pkuyo.CanKit.ZLG.Options;
using Pkuyo.CanKit.ZLG.Transceivers;

namespace Pkuyo.CanKit.ZLG
{

    public abstract class ZlgCanProvider : ICanModelProvider
    {
        public abstract DeviceType DeviceType { get; }

        public virtual CanFeature StaticFeatures => CanFeature.CanClassic | CanFeature.Filters | CanFeature.ErrorCounters;

        public virtual ZlgFeature ZlgFeature => ZlgFeature.None;

        public ICanFactory Factory => CanRegistry.Registry.Factory("Zlg");

        public (IDeviceOptions, IDeviceInitOptionsConfigurator) GetDeviceOptions()
        {
            var option = new ZlgDeviceOptions(this);
            var cfg = new ZlgDeviceInitOptionsConfigurator();
            cfg.Init(option);
            return (option, cfg);
        }

        public (IBusOptions, IBusInitOptionsConfigurator) GetChannelOptions(int channelIndex)
        {
            var option = new ZlgBusOptions(this)
            {
                ChannelIndex = channelIndex
            };
            var cfg = new ZlgBusInitConfigurator();
            cfg.Init(option);
            return (option, cfg);
        }
    }
}
