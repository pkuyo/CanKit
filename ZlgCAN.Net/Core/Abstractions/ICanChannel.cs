using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZlgCAN.Net.Core.Models;

namespace ZlgCAN.Net.Core.Abstractions
{
    public interface IChannelCapabilities
    {
        CanFrameFlag SupportFlag { get; }
    }

    public interface ICanChannel : IDisposable, IChannelCapabilities
    {


        void Start();

        void Reset();

        void CleanBuffer();

        uint Transmit(params CanFrameBase[] frames);

        IEnumerable<CanReceiveData> ReceiveAll(CanFrameFlag filterFlag = CanFrameFlag.Any);

        IEnumerable<CanReceiveData> Receive(uint count = 1, int timeOut = -1, CanFrameFlag filterFlag = CanFrameFlag.Any);

        uint CanReceiveCount(CanFrameFlag filterFlag);

        public IntPtr NativePtr { get; }

        public CanChannelConfig Config { get; }

    }
}
