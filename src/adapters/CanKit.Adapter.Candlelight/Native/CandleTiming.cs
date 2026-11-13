using System;

namespace CanKit.Adapter.Candlelight.Native;

internal static class CandleTiming
{
    internal static CandleNative.BitTiming Calculate(CandleNative.Capabilities capabilities, uint bitrate, ushort samplePointPermille = 875)
    {
        if (bitrate == 0)
            throw new ArgumentOutOfRangeException(nameof(bitrate));
        if (samplePointPermille == 0 || samplePointPermille >= 1000)
            throw new ArgumentOutOfRangeException(nameof(samplePointPermille));
        if (capabilities.Clock == 0 || capabilities.BrpIncrement == 0 || capabilities.SjwMax == 0 ||
            capabilities.Tseg1Min == 0 || capabilities.Tseg2Min == 0 ||
            capabilities.Tseg1Min > capabilities.Tseg1Max || capabilities.Tseg2Min > capabilities.Tseg2Max)
            throw new InvalidOperationException("The Candle device reports invalid CAN timing capabilities.");

        var best = new CandleNative.BitTiming();
        ulong bestQuanta = 0;
        long bestSampleError = 0;
        for (ulong tseg1 = capabilities.Tseg1Min; tseg1 <= capabilities.Tseg1Max; tseg1++)
        {
            for (ulong tseg2 = capabilities.Tseg2Min; tseg2 <= capabilities.Tseg2Max; tseg2++)
            {
                var quanta = 1UL + tseg1 + tseg2;
                var divisor = (ulong)bitrate * quanta;
                if (capabilities.Clock % divisor != 0)
                    continue;
                var brp = capabilities.Clock / divisor;
                if (brp < capabilities.BrpMin || brp > capabilities.BrpMax || brp % capabilities.BrpIncrement != 0)
                    continue;
                var sampleError = Math.Abs(1000L * (1 + (long)tseg1) - samplePointPermille * (long)quanta);
                if (bestQuanta != 0 && (sampleError * (long)bestQuanta > bestSampleError * (long)quanta ||
                    (sampleError * (long)bestQuanta == bestSampleError * (long)quanta && quanta <= bestQuanta)))
                    continue;
                bestQuanta = quanta;
                bestSampleError = sampleError;
                best = new CandleNative.BitTiming
                {
                    PropSeg = (uint)(tseg1 / 2),
                    PhaseSeg1 = (uint)(tseg1 - tseg1 / 2),
                    PhaseSeg2 = (uint)tseg2,
                    Sjw = 1,
                    Brp = (uint)brp
                };
            }
        }
        if (bestQuanta == 0)
            throw new InvalidOperationException($"The Candle device cannot generate {bitrate} bps from its {capabilities.Clock} Hz CAN clock.");
        return best;
    }
}
