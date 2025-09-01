using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net
{
    public static class Can
    {
        public static CanSession<ICanDevice,ICanChannel> Open(DeviceType deviceType,
            Action<DeviceInitOptionsConfigurator<IDeviceOptions>> configure = null)
        {
            return Open<ICanDevice,ICanChannel,IDeviceOptions,DeviceInitOptionsConfigurator<IDeviceOptions>>(deviceType, configure);
        }

        public static CanSession<TDevice,TChannel> Open<TDevice,TChannel,TDeviceOptions,TOptionCfg>
        (DeviceType deviceType,
            Action<TOptionCfg> configure = null,
            Func<TDevice,ICanModelProvider,CanSession<TDevice,TChannel>> sessionBuilder = null)
            where TDevice :  class, ICanDevice
            where TChannel : class, ICanChannel
            where TDeviceOptions : class, IDeviceOptions 
            where TOptionCfg : IDeviceInitOptionsConfigurator<IDeviceOptions>
        {
            var provider = CanCore.Registry.Resolve(deviceType);
            var factory = provider.Factory;

            var (options, cfg) = provider.GetDeviceOptions();
            if (options is not TDeviceOptions specOptions ||
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
    
    public interface IDeviceProfile
    {
        Type DeviceType { get; }
        Type ChannelType { get; }
    }


    public class CanSession<TCanDevice,TCanChannel>(TCanDevice device, ICanModelProvider provider) : IDisposable
    where TCanDevice : class, ICanDevice
    where TCanChannel : class, ICanChannel
    {
        public TCanChannel this[int index] => innerChannels[index];

        public void Open()
        {
            Device.OpenDevice();
        }

        public void Close()
        {
            Device.CloseDevice();
        }

        public TCanChannel CreateChannel(int index, uint baudRate)
        {
            return CreateChannel<IChannelOptions,ChannelInitOptionsConfigurator<IChannelOptions>>(index, 
                cfg => cfg.Baud(baudRate));
        }
        
        /*
        public TCanChannel CreateChannel(int index, Action<ChannelInitOptionsConfigurator> configure = null)
        {
            return CreateChannel<IChannelOptions,ChannelInitOptionsConfigurator>(index, configure);
        }
        */

        public TCanChannel CreateChannel<TChannelOptions,TOptionCfg>(int index,
            Action<TOptionCfg> configure = null)
            where TChannelOptions : class, IChannelOptions
            where TOptionCfg : IChannelInitOptionsConfigurator<IChannelOptions>
        {
            var (options, cfg) = Provider.GetChannelOptions(index);
            if (options is not TChannelOptions specOptions ||
                cfg is not TOptionCfg specCfg)
                throw new Exception(); //TODO: 异常处理
            

            if (configure != null)
            {
                configure(specCfg);
            }
            
            var transceivers = Provider.CreateTransceivers();

            var innerChannel = (TCanChannel)provider.Factory.CreateChannel(Device, options, transceivers);
            if (innerChannel != null)
            {
                innerChannels.Add(index, innerChannel);
                return innerChannel;
            }
            return null;

        }

        public void Dispose()
        {
            Device.Dispose();
            foreach (var channel in innerChannels)
            {
                channel.Value.Dispose();
            }
            
            innerChannels.Clear();
        }
        
        public bool IsDeviceOpen => Device.IsDeviceOpen;

        protected Dictionary<int, TCanChannel> innerChannels = new();
        
        protected TCanDevice Device { get; } = device;

        protected ICanModelProvider Provider { get; } = provider;
    }
    
    
}