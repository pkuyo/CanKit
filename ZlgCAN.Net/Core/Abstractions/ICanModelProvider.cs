using System;
using System.Collections.Generic;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions
{

    public interface ICanApplier
    {
        bool ApplyOne<T>(string name, T value);
    }

    public interface ICanOptions
    {
        void Apply(ICanApplier applier, bool force = false);
    }
    
    public interface IDeviceOptions : ICanOptions { ZlgDeviceKind DeviceType { get; } }
    public interface IChannelInitOptions : ICanOptions { int ChannelIndex { get; set; } }

    public interface IChannelRuntimeOptions : ICanOptions { int ChannelIndex { get; }}
    
    public interface ICanModelProvider
    {
        public ZlgDeviceKind DeviceType { get; }
        
        IDeviceOptions CreateDeviceOptions();
        IChannelInitOptions CreateChannelInitOptions(int channelIndex);
        IChannelRuntimeOptions CreateChannelRuntimeOptions(int channelIndex);
        
        IEnumerable<ITransceiver> CreateTransceivers();
    }
}