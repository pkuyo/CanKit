using System;
using System.Collections.Generic;
using ZlgCAN.Net.Core;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net;

public static class Can
{
    public static CanSession Open(DeviceType deviceType, Action<DeviceInitOptionsConfigurator> configure = null)
    {
        var provider = CanCore.Registry.Resolve(deviceType);
        var factory = provider.Factory;

        var options = provider.GetDeviceOptions();
        if(configure != null)
            configure(new DeviceInitOptionsConfigurator(options, provider.Features));
        var session = new CanSession(factory.CreateDevice(options),provider);
        session.Open();
        return session;
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

        public CanChannel CreateChannel(int index, Action<ChannelInitOptionsConfigurator> configure = null)
        {
            var options = Provider.GetChannelOptions(index);
            if(configure != null)
                configure(new ChannelInitOptionsConfigurator(options, provider.Features));
            
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
        
        protected Dictionary<int,ICanChannel> innerChannels = new ();
        
        protected Dictionary<int,CanChannel> channels = new ();
        protected ICanDevice Device { get; } = device;
        
        protected ICanModelProvider Provider { get; } = provider;
    }
}
