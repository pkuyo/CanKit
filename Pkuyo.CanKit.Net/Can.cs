using System;
using System.Collections.Generic;
using Pkuyo.CanKit.Net.Core;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net
{
    public static class Can
    {
        public static CanSession Open(DeviceType deviceType, Action<DeviceInitOptionsConfigurator> configure = null)
        {
            return Open<IDeviceOptions>(deviceType, configure);
        }

        public static CanSession Open<TDeviceOptions>(DeviceType deviceType,
            Action<DeviceInitOptionsConfigurator<TDeviceOptions>> configure = null,
            Func<ICanDevice,ICanModelProvider,CanSession> sessionBuilder = null)
            where TDeviceOptions : class, IDeviceOptions
        {
            var provider = CanCore.Registry.Resolve(deviceType);
            var factory = provider.Factory;

            var options = provider.GetDeviceOptions();
            if (options is not TDeviceOptions specOptions)
                throw new Exception(); //TODO: 异常处理
            if (configure != null)
                configure(new DeviceInitOptionsConfigurator<TDeviceOptions>(specOptions, provider.Features));
            var session = sessionBuilder == null
                ? new CanSession(factory.CreateDevice(options), provider)
                : sessionBuilder(factory.CreateDevice(options), provider);
            session.Open();
            return session;
        }

    }

    public class CanChannel(ICanChannel channel)
    {
        public void Start()
            => channel.Start();

        public void Reset()
            => channel.Reset();

        public void Stop()
            => channel.Stop();

        public void CleanBuffer()
            => channel.CleanBuffer();

        public uint Transmit(params CanFrameBase[] frames)
            => channel.Transmit(frames);

        public IEnumerable<CanReceiveData> ReceiveAll(CanFrameType filterType)
            => channel.ReceiveAll(filterType);

        public IEnumerable<CanReceiveData> Receive(CanFrameType filterType, uint count = 1, int timeOut = -1)
            => channel.Receive(filterType, count, timeOut);

        public uint CanReceiveCount(CanFrameType filterType)
            => channel.CanReceiveCount(filterType);

        public ChannelRTOptionsConfigurator Options => channel.Options;
    }

    public class CanSession(ICanDevice device, ICanModelProvider provider) : IDisposable
    {
        public CanChannel this[int index] => channels[index];

        public void Open()
        {
            Device.OpenDevice();
        }

        public void Close()
        {
            Device.CloseDevice();
        }

        public CanChannel CreateChannel(int index, uint baudRate)
        {
            return CreateChannel(index, cfg => cfg.Baud(baudRate));
        }

        public CanChannel CreateChannel(int index, Action<ChannelInitOptionsConfigurator> configure = null)
        {
            return CreateChannel<IChannelOptions>(index, configure);
        }

        public CanChannel CreateChannel<TChannelOptions>(int index,
            Action<ChannelInitOptionsConfigurator<TChannelOptions>> configure = null)
            where TChannelOptions : class, IChannelOptions
        {
            var options = Provider.GetChannelOptions(index);
            if (options is not TChannelOptions specOptions)
                throw new Exception(); //TODO: 异常处理
            if (configure != null)
                configure(new ChannelInitOptionsConfigurator<TChannelOptions>(specOptions, provider.Features));

            var transceivers = Provider.CreateTransceivers();

            var innerChannel = provider.Factory.CreateChannel(Device, options, transceivers);
            if (innerChannel != null)
            {
                var channel = new CanChannel(innerChannel);
                innerChannels.Add(index, innerChannel);
                channels.Add(index, channel);
                return channel;
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

            channels.Clear();
            innerChannels.Clear();
        }
        
        public bool IsDeviceOpen => Device.IsDeviceOpen;

        protected Dictionary<int, ICanChannel> innerChannels = new();

        protected Dictionary<int, CanChannel> channels = new();
        protected ICanDevice Device { get; } = device;

        protected ICanModelProvider Provider { get; } = provider;
    }
}