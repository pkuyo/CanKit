using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Exceptions;

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
                throw new CanDeviceNotOpenException();

            if (InnerChannels.TryGetValue(index, out var channel) && channel != null)
                return channel;

            var (options, cfg) = Provider.GetChannelOptions(index);
            if (options is not TChannelOptions typedOptions)
            {
                throw new CanOptionTypeMismatchException(
                    CanKitErrorCode.ChannelOptionTypeMismatch,
                    typeof(TChannelOptions),
                    options?.GetType() ?? typeof(IChannelOptions),
                    $"channel {index}");
            }

            if (cfg is not TOptionCfg specCfg)
            {
                throw new CanOptionTypeMismatchException(
                    CanKitErrorCode.ChannelOptionTypeMismatch,
                    typeof(TOptionCfg),
                    cfg?.GetType() ?? typeof(IChannelInitOptionsConfigurator),
                    $"channel {index} configurator");
            }


            configure?.Invoke(specCfg);

            var transceivers = Provider.Factory.CreateTransceivers(Device.Options, specCfg);
            if (transceivers == null)
            {
                throw new CanFactoryException(
                    CanKitErrorCode.TransceiverMismatch,
                    $"Factory '{Provider.Factory.GetType().FullName}' returned null when creating a transceiver for channel {index}.");
            }

            var createdChannel = Provider.Factory.CreateChannel(Device, typedOptions, transceivers);
            if (createdChannel == null)
            {
                throw new CanChannelCreationException(
                    $"Factory '{Provider.Factory.GetType().FullName}' returned null when creating channel {index}.");
            }

            if (createdChannel is not TCanChannel innerChannel)
            {
                throw new CanChannelCreationException(
                    $"Factory produced channel type '{createdChannel.GetType().FullName}' which cannot be assigned to '{typeof(TCanChannel).FullName}'.");
            }

            InnerChannels.Add(index, innerChannel);
            return innerChannel;

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