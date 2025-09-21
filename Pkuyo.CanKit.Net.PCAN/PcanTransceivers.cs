using Peak.Can.Basic;
using Peak.Can.Basic.BackwardCompatibility;
using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;

namespace Pkuyo.CanKit.Net.PCAN;

public sealed class PcanClassicTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        var ch = (PcanBus)channel;
        uint sent = 0;
        foreach (var item in frames)
        {
            if (item.CanFrame is not CanClassicFrame cf)
                throw new InvalidOperationException("PCAN classic transceiver requires CanClassicFrame.");

            var type = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            if (cf.IsExtendedFrame) type |= TPCANMessageType.PCAN_MESSAGE_EXTENDED;
            if (cf.IsRemoteFrame) type |= TPCANMessageType.PCAN_MESSAGE_RTR;

            var msg = new TPCANMsg
            {
                ID = cf.ID,
                LEN = (byte)Math.Min(8, (int)cf.Dlc),
                MSGTYPE = type,
                DATA = new byte[8]
            };
            var src = cf.Data.Span;
            for (int i = 0; i < msg.LEN; i++) msg.DATA[i] = src[i];

            var st = PCANBasic.Write(ch.Handle, ref msg);
            if (st == TPCANStatus.PCAN_ERROR_OK) sent++;
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
    {
        var ch = (PcanBus)channel;
        var list = new List<CanReceiveData>();
        for (int i = 0; i < count; i++)
        {
            var st = PCANBasic.Read(ch.Handle, out TPCANMsg msg, out TPCANTimestamp ts);
            if (st == TPCANStatus.PCAN_ERROR_QRCVEMPTY)
                break;
            if (st != TPCANStatus.PCAN_ERROR_OK)
                break;

            bool isExt = (msg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_EXTENDED) != 0;
            bool isRtr = (msg.MSGTYPE & TPCANMessageType.PCAN_MESSAGE_RTR) != 0;

            var data = new byte[msg.LEN];
            Array.Copy(msg.DATA, data, msg.LEN);

            var frame = new CanClassicFrame(msg.ID, data, isExt) { IsRemoteFrame = isRtr };

            ulong ticks;
            try
            {
                // Convert PCAN relative timestamp to ticks (best effort)
                // millis_overflow increments when millis wraps 32-bit
                var microsTotal = ((ulong)ts.millis_overflow << 32) * 1000UL + (ulong)ts.millis * 1000UL + (ulong)ts.micros;
                ticks = (ulong)(microsTotal * 10); // 1 tick = 100 ns
            }
            catch
            {
                ticks = (ulong)DateTime.UtcNow.Ticks;
            }

            list.Add(new CanReceiveData(frame) { recvTimestamp = ticks });
        }
        return list;
    }
}

public sealed class PcanFdTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        throw new NotSupportedException("PCAN FD not integrated yet.");
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
    {
        throw new NotSupportedException("PCAN FD not integrated yet.");
    }
}
