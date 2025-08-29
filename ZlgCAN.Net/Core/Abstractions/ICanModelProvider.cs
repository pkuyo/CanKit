using System;
using System.Collections.Generic;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions
{

    public enum FilterMode
    {
        Standard,
        Extend,
    }
    public interface ICanApplier
    {
        bool ApplyOne<T>(string name, T value);
    }
    

    public interface ICanOptions
    {
        ICanModelProvider Provider { get; }
        
        bool HasChanges { get; }
        IEnumerable<string> GetChangedNames();
        void ClearChanges();
        
        void Apply(ICanApplier applier, bool force = false);
    }

    

    public interface IDeviceOptions : ICanOptions
    {
        DeviceType DeviceType { get; set; }
        
        uint TxTimeOut { get; set; }
        
        bool MergeReceive { get; set; }
    }
    
    public interface IChannelOptions : ICanOptions
    {
        
        //CanFd
        bool CanFdStandard { get; set; }
        uint ABitBaudRate { get; set; }
        uint DBitBaudRate { get; set; }
        
        uint BaudRate { get; set; }
        int ChannelIndex { get; set; }
        
        bool InternalResistance { get; set; }
        
        //Filter
        FilterMode FilterMode { get; set; }
        uint FilterStart { get; set; }
        uint FilterEnd { get; set; }
        bool FilterEnable { get; set; }
      
        uint AccCode { get; set; }
        
        uint AccMask { get; set; }
        
    }
    
    
    public interface ICanModelProvider
    {
        DeviceType DeviceType { get; }
        
        CanFeature Features { get; }

        ICanFactory Factory { get; }
        
        IDeviceOptions GetDeviceOptions();
        
        IChannelOptions GetChannelOptions(int channelIndex);
        
        
        IEnumerable<ITransceiver> CreateTransceivers();
        
    }
}