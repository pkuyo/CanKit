using System.Collections.Generic;
using System.Runtime.InteropServices;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.SocketCAN.Native;

namespace Pkuyo.CanKit.SocketCAN;

public sealed class SocketCanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames,
        int _ = 0)
    {
        if (frames.Single().CanFrame is not CanClassicFrame cf)
            throw new InvalidOperationException("SocketCanTransceiver requires CanClassicFrame for transmission");
        unsafe
        {


            var frame = new Libc.can_frame
            {
                can_id = cf.RawID,
                can_dlc = cf.Dlc,
                __pad = 0,
                __res0 = 0,
                __res1 = 0,
            };
            var src = cf.Data.Span;
            var size = Marshal.SizeOf<Libc.can_frame>();

            fixed (byte* pSrc = src)
            {
                Buffer.MemoryCopy(pSrc, frame.data, 8, 8);
            }

            var result = Libc.write(((SocketCanChannel)channel).FileDescriptor, &frame, (ulong)size);
            return result switch
            {
                > 0 => 1,
                0 => 0,
                _ => throw new Exception() //TODO:异常处理
            };
        }

    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        var size = Marshal.SizeOf<Libc.can_frame>();
        var result = new List<CanReceiveData>();
        unsafe
        {
            var ptr = stackalloc Libc.can_frame[1];
            var n = Libc.read(((SocketCanChannel)channel).FileDescriptor,ptr , (ulong)size);
            if (n <= 0)
            {
                //TODO:异常处理
                return result;
            }
            
            var data = new byte[ptr->can_dlc];
            
            fixed (byte* pDst = data)
            { 
                Buffer.MemoryCopy(ptr->data,pDst,  8, 8);
            }
            result.Add(new CanReceiveData(new CanClassicFrame(ptr->can_id, data))
                { recvTimestamp = (ulong)DateTime.Now.Ticks });
        }
        return result;
    }
}

public sealed class SocketCanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanChannel<IChannelRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames,
        int _ = 0)
    {
        if (frames.Single().CanFrame is not CanFdFrame ff)
            throw new InvalidOperationException("SocketCanFdTransceiver requires CanFdFrame for transmission");
        var frame = new Libc.canfd_frame
        {
            can_id = ff.RawID,
            len = (byte)CanFdFrame.DlcToLen(ff.Dlc),
            flags = (byte)((ff.BitRateSwitch ? Libc.CANFD_BRS : 0) | (ff.ErrorStateIndicator ? Libc.CANFD_ESI : 0)),
            __res0 = 0,
            __res1 = 0,
        };
        
        unsafe
        {
            var src = ff.Data.Span;
            fixed (byte* pSrc = src)
            {
                Buffer.MemoryCopy(pSrc, frame.data, 64, 64);
            }

            var size = Marshal.SizeOf<Libc.canfd_frame>();
        
            var result = Libc.write(((SocketCanChannel)channel).FileDescriptor, &frame, (ulong)size);
            return result switch
            {
                > 0 => 1,
                0 => 0,
                _ => throw new Exception() //TODO:异常处理
            };
        }
    }

    public IEnumerable<CanReceiveData> Receive(ICanChannel<IChannelRTOptionsConfigurator> channel, uint count = 1, int _ = -1)
    {
        var size = Marshal.SizeOf<Libc.canfd_frame>();
        var result = new List<CanReceiveData>();
        unsafe
        {


            var ptr = stackalloc Libc.canfd_frame[1];

            var n = Libc.read(((SocketCanChannel)channel).FileDescriptor, ptr, (ulong)size);
            if (n <= 0)
            {
                //TODO:异常处理
                return result;
            }
            
            var data = new byte[ptr->len];

            fixed (byte* pDst = data)
            {
                Buffer.MemoryCopy(ptr->data, pDst, 64, 64);
            }
            
            // keep classic frame here for compatibility with existing code paths
            result.Add(new CanReceiveData(new CanFdFrame(ptr->can_id, data,
                    (ptr->flags & Libc.CANFD_BRS) != 0,
                    (ptr->flags & Libc.CANFD_ESI) != 0))
                     { recvTimestamp = (ulong)DateTime.Now.Ticks });
        }

        return result;
    }
}
