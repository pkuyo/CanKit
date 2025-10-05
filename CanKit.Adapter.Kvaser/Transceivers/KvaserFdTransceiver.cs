using System.Collections.Generic;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserFdTransceiver : ITransceiver
{
    public uint Transmit(ICanBus<IBusRTOptionsConfigurator> channel, IEnumerable<CanTransmitData> frames, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        uint sent = 0;
        foreach (var item in frames)
        {
            var data = item.CanFrame.Data.ToArray();
            var dlc = item.CanFrame.Dlc;
            var flags = 0U;
            if (item.CanFrame is CanFdFrame fd)
            {
                flags = Canlib.canFDMSG_FDF;
                if (fd.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                if (fd.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
                if (fd.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
            }
            else if (item.CanFrame is CanClassicFrame classic)
            {
                if (classic.IsExtendedFrame) flags |= (uint)Canlib.canMSG_EXT;
                if (classic.IsRemoteFrame) flags |= (uint)Canlib.canMSG_RTR;
            }

            var st = Canlib.canWrite(ch.Handle, (int)item.CanFrame.ID, data, dlc, (int)flags);
            if (st == Canlib.canStatus.canOK)
            {
                sent++;
            }
            else
            {
                //TODO:异常处理
            }
        }
        return sent;
    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> channel, uint count = 1, int timeOut = 0)
    {
        var ch = (KvaserBus)channel;
        var list = new List<CanReceiveData>();
        var timeout = timeOut < 0 ? -1 : timeOut;
        for (int i = 0; i < count; i++)
        {
            byte[] data = new byte[64];
            var st = timeout > 0
                ? Canlib.canReadWait(ch.Handle, out var id, data, out var dlc, out var flags, out var time, timeout)
                : Canlib.canRead(ch.Handle, out id, data, out dlc, out flags, out time);

            if (st == Canlib.canStatus.canOK)
            {
                var isFd = (flags & (int)Canlib.canFDMSG_FDF) != 0;

                var isExt = (flags & (int)Canlib.canMSG_EXT) != 0;
                var brs = (flags & (int)Canlib.canFDMSG_BRS) != 0;
                var esi = (flags & (int)Canlib.canFDMSG_ESI) != 0;
                var isErr = (flags & (int)Canlib.canMSG_ERROR_FRAME) != 0;
                var isRtr = (flags & (int)Canlib.canMSG_RTR) != 0;
                var len = CanFdFrame.DlcToLen((byte)dlc);
                if (data.Length > len)
                {
                    Array.Resize(ref data, len);
                }

                ICanFrame frame;
                if (isFd)
                {
                    frame = new CanFdFrame((uint)id, data, brs, esi)
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr };
                }
                else
                {
                    frame = new CanClassicFrame((uint)id, data)
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr, IsRemoteFrame = isRtr };
                }

                // Convert using configured timer_scale (microseconds per unit)
                var kch = (KvaserBus)channel;
                var ticks = (long)time * kch.Options.TimerScaleMicroseconds * 10L; // us -> ticks
                list.Add(new CanReceiveData(frame) { ReceiveTimestamp = TimeSpan.FromTicks(ticks) });
            }
            else if (st == Canlib.canStatus.canERR_NOMSG)
            {
                break;
            }
            else
            {
                //TODO:异常处理
                break;
            }
        }
        return list;
    }
}
