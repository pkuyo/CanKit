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

        void CleanBuffer();

        uint Transmit(params CanFrameBase[] frames);

        IEnumerable<CanReceiveData> ReceiveAll(CanFilterType filterType);

        IEnumerable<CanReceiveData> Receive(CanFilterType filterType, uint count = 1, int timeOut = -1);

        uint CanReceiveCount(CanFilterType filterType);

        public IntPtr NativePtr { get; }

        public IChannelRuntimeOptions Options { get; }

    }
}
