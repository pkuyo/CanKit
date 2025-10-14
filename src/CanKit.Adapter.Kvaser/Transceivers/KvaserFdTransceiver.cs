using System.Collections.Generic;
using CanKit.Adapter.Kvaser.Native;
using CanKit.Adapter.Kvaser.Utils;
using CanKit.Core.Abstractions;
using CanKit.Core.Definitions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Transceivers;

public sealed class KvaserFdTransceiver : ITransceiver
{
    public int Transmit(ICanBus<IBusRTOptionsConfigurator> bus, IEnumerable<ICanFrame> frames, int _ = 0)
    {
        var ch = (KvaserBus)bus;
        int sent = 0;

        foreach (var item in frames)
        {
            var dlc = item.Data.Length;
            var flags = 0U;
            if (item is CanFdFrame fd)
            {
                flags = Canlib.canFDMSG_FDF;
                flags |= (bus.Options.TxRetryPolicy == TxRetryPolicy.NoRetry) ? (uint)Canlib.canMSG_SINGLE_SHOT : 0;
                if (fd.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                if (fd.BitRateSwitch) flags |= Canlib.canFDMSG_BRS;
                if (fd.ErrorStateIndicator) flags |= Canlib.canFDMSG_ESI;
            }
            else if (item is CanClassicFrame classic)
            {
                if (classic.IsExtendedFrame) flags |= Canlib.canMSG_EXT;
                if (classic.IsRemoteFrame) flags |= Canlib.canMSG_RTR;
            }

            Canlib.canStatus st;
            unsafe
            {
                fixed (byte* ptr = item.Data.Span)
                {
                    st = CanlibNative.canWrite(ch.Handle, (int)item.ID, ptr, (uint)dlc, flags);
                }
            }

            if (st == Canlib.canStatus.canOK)
            {
                sent++;
            }
            else if (st is Canlib.canStatus.canERR_TXBUFOFL or Canlib.canStatus.canERR_TIMEOUT)
            {
                return sent;
            }
            else
            {
                var msg = "Failed to write frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canWrite", msg);
            }
        }

        return sent;



    }

    public IEnumerable<CanReceiveData> Receive(ICanBus<IBusRTOptionsConfigurator> bus, int count = 1, int _ = 0)
    {
        var ch = (KvaserBus)bus;
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        int recCount = 0;
        while (true)
        {
            byte[] data = new byte[64];
            int id;
            int flags;
            long time;
            int dlc;
            var st = Canlib.canRead(ch.Handle, out id, data, out dlc, out flags, out time);
            if (st == Canlib.canStatus.canOK)
            {
                var isFd = (flags & Canlib.canFDMSG_FDF) != 0;
                var isExt = (flags & Canlib.canMSG_EXT) != 0;
                var brs = (flags & Canlib.canFDMSG_BRS) != 0;
                var esi = (flags & Canlib.canFDMSG_ESI) != 0;
                var isErr = (flags & Canlib.canMSG_ERROR_FRAME) != 0;
                var isRtr = (flags & Canlib.canMSG_RTR) != 0;

                ICanFrame frame = isFd
                    ? new CanFdFrame(id, new ArraySegment<byte>(data, 0, CanFdFrame.DlcToLen((byte)dlc)), brs, esi)
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr }
                    : new CanClassicFrame(id, new ArraySegment<byte>(data, 0, dlc))
                    { IsExtendedFrame = isExt, IsErrorFrame = isErr, IsRemoteFrame = isRtr };

                var kch = (KvaserBus)bus;
                var ticks = time * kch.Options.TimerScaleMicroseconds * 10L; // us -> ticks
                yield return new CanReceiveData(frame) { ReceiveTimestamp = TimeSpan.FromTicks(ticks) };

                recCount++;
                if (recCount == count)
                    yield break;
            }
            else if (st == Canlib.canStatus.canERR_NOMSG)
            {
                yield break;
            }
            else
            {
                var msg = "Failed to read frame";
                if (Canlib.canGetErrorText(st, out var str) == Canlib.canStatus.canOK)
                    msg += $". Message:{str}";
                KvaserUtils.ThrowIfError(st, "canRead", msg);
                yield break;
            }
        }

    }
}
