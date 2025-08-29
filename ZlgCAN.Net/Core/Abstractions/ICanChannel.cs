using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZlgCAN.Net.Core.Definitions;

namespace ZlgCAN.Net.Core.Abstractions
{
  

    public interface ICanChannel : IDisposable
    {
        void Start();

        void Reset();
        
        void Stop();

        void CleanBuffer();

        uint Transmit(params CanFrameBase[] frames);

        IEnumerable<CanReceiveData> ReceiveAll(CanFrameType filterType);

        IEnumerable<CanReceiveData> Receive(CanFrameType filterType, uint count = 1, int timeOut = -1);

        uint CanReceiveCount(CanFrameType filterType);

        public ChannelRTOptionsConfigurator Options { get; }

    }
}
