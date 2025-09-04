using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Registry;

namespace Pkuyo.CanKit.Net
{
    public static class Can
    {
        public static CanSession<ICanDevice,ICanChannel> Open(DeviceType deviceType, Action<IDeviceInitOptionsConfigurator> configure = null)
        {
            return Open<ICanDevice,ICanChannel,IDeviceOptions,IDeviceInitOptionsConfigurator>(deviceType, configure);
        }
        
        public static CanSession<TDevice,TChannel> Open<TDevice,TChannel,TDeviceOptions,TOptionCfg>
        (DeviceType deviceType,
            Action<TOptionCfg> configure = null,
            Func<TDevice,ICanModelProvider,CanSession<TDevice,TChannel>> sessionBuilder = null)
            where TDevice :  class, ICanDevice
            where TChannel : class, ICanChannel
            where TDeviceOptions : class, IDeviceOptions 
            where TOptionCfg : IDeviceInitOptionsConfigurator
        {
            var provider = CanRegistry.Registry.Resolve(deviceType);
            var factory = provider.Factory;

            var (options, cfg) = provider.GetDeviceOptions();
            if (options is not TDeviceOptions ||
                cfg is not TOptionCfg specCfg)
                throw new Exception(); //TODO: 异常处理
            
            if (configure != null)
            {
                configure(specCfg);
            }
            
            if(factory.CreateDevice(options) is not TDevice device)
                throw new Exception(); //TODO: 异常处理

            var session = sessionBuilder == null
                ? new CanSession<TDevice,TChannel>(device, provider)
                : sessionBuilder(device, provider);
            
            session.Open();
            return session;
        }

    }
}