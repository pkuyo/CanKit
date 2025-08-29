

using System;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Utils;

namespace ZlgCAN.Net.Core.Definitions
{
    
    public struct ChannelRTOptionsConfigurator(IChannelOptions options)
    {
        
        public int ChannelIndex
        {
            get;
        }
        
        public int BuadRate
        {
            get;
        }
        
        public int ABitRate
        {
            get;
        }
        
        public int DBitRate
        {
            get;
        }
        
        //....
        public ChannelRTOptionsConfigurator Filter(uint start, uint end)
        {
      
            return this;
        }
        public ChannelRTOptionsConfigurator AccMask( uint accCode, uint accMask )
        {
            return this;
        }
     

        public bool InternalResistance
        {
            get;
            set;
        }
    }
    
    public struct ChannelInitOptionsConfigurator(IChannelOptions options,CanFeature canFeature)
    {
        public ChannelInitOptionsConfigurator Classic(int baud)
        {
            canFeature.CheckFeature(CanFeature.CanClassic);
            return this;
        }
        // FD
        public ChannelInitOptionsConfigurator Fd(int abit, int dbit)
        {
            canFeature.CheckFeature(CanFeature.CanFd);
            return this;
        }

        // AccMask
        public ChannelInitOptionsConfigurator AccMask( uint accCode, uint accMask )
        {
            canFeature.CheckFeature(CanFeature.AccMask);   
            return this;
        }
        // Filter
        public ChannelInitOptionsConfigurator Filter(uint start, uint end, bool ack = true )
        {
            canFeature.CheckFeature(CanFeature.Filters);   
            return this;
        }
        
        // BusUsage
        public ChannelInitOptionsConfigurator BusUsage(int periodMs = 1000)
        {
            canFeature.CheckFeature(CanFeature.BusUsage);
            return this;
        }

        // Ethernet
        public ChannelInitOptionsConfigurator Ethernet(string ip, int localPort, EthernetWorkMode mode)
        {
            canFeature.CheckFeature(CanFeature.Ethernet);
            return this;
        }
    }

    public struct DeviceInitOptionsConfigurator(IDeviceOptions options, CanFeature canFeature)
    {
        public DeviceInitOptionsConfigurator TxTimeOut(uint ms)
        {
            return this;
        }
        public DeviceInitOptionsConfigurator MergeReceive(bool enable)
        {
            return this;
        }
    }

}