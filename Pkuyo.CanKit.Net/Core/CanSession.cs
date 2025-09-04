using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;

namespace Pkuyo.CanKit.Net.Core;

public class CanSession<TCanDevice,TCanChannel>(TCanDevice device, ICanModelProvider provider) : IDisposable
    where TCanDevice : class, ICanDevice
    where TCanChannel : class, ICanChannel
    {
        public TCanChannel this[int index] => InnerChannels[index];

        public bool Open()
        {
            return Device.OpenDevice();
        }

        public void Close()
        {
            Device.CloseDevice();
        }

        public TCanChannel CreateChannel(int index, uint baudRate)
        {
            return CreateChannel<IChannelOptions,IChannelInitOptionsConfigurator>(index, 
                cfg => cfg.Baud(baudRate));
        }
        
        
        public TCanChannel CreateChannel(int index, Action<IChannelInitOptionsConfigurator> configure = null)
        {
            return CreateChannel<IChannelOptions,IChannelInitOptionsConfigurator>(index, configure);
        }


        protected TCanChannel CreateChannel<TChannelOptions,TOptionCfg>(int index,
            Action<TOptionCfg> configure = null)
            where TChannelOptions : class, IChannelOptions
            where TOptionCfg : IChannelInitOptionsConfigurator
        {
            if (!IsDeviceOpen)
                throw new Exception(); //TODO: 异常处理

            if (InnerChannels.TryGetValue(index, out var channel) && channel != null)
                return channel;
            
            var (options, cfg) = Provider.GetChannelOptions(index);
            if (options is not TChannelOptions ||
                cfg is not TOptionCfg specCfg)
                throw new Exception(); //TODO: 异常处理
            

            if (configure != null)
            {
                configure(specCfg);
            }
            
            var transceivers = Provider.Factory.CreateTransceivers(Device.Options, specCfg);

            var innerChannel = (TCanChannel)Provider.Factory.CreateChannel(Device, options, transceivers);
            if (innerChannel != null)
            {
                InnerChannels.Add(index, innerChannel);
                return innerChannel;
            }
            return null;

        }

        public void Dispose()
        {
            Device.Dispose();
            foreach (var channel in InnerChannels)
            {
                channel.Value.Dispose();
            }
            
            InnerChannels.Clear();
        }
        
        public bool IsDeviceOpen => Device.IsDeviceOpen;

        protected readonly Dictionary<int, TCanChannel> InnerChannels = new();
        
        protected TCanDevice Device { get; } = device;

        protected ICanModelProvider Provider { get; } = provider;
    }