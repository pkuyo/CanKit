using System;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils;

public static class BitTimingSolver
{
    /// <summary>
    /// 根据 f_clock / bitrate / samplePoint 计算一组 BRP/TSEG1/TSEG2/SJW。
    /// 返回最优（采样点误差最小）的解；如无精确整数解则抛出异常或放宽 limits。
    /// samplePoint：0~1（如 0.8 表示 80%）
    /// </summary>
    public static BitTimingSegments FromSamplePoint(
        uint fClockMHz,
        uint targetBitrate,
        double samplePoint,
        BitTimingLimits? limits = null,
        uint? fixedSjw = null      // 传 null 时自动取 min(4, tseg2)
    )
    {
        if (samplePoint <= 0 || samplePoint >= 1) throw new ArgumentOutOfRangeException(nameof(samplePoint));

        var fClockHz = (long)fClockMHz * 1_000_000;

        var L = limits ?? new BitTimingLimits();
        BitTimingSegments? best = null;
        var bestErr = double.MaxValue;

        for (var ntq = (uint)L.NtqMin; ntq <= L.NtqMax; ntq++)
        {
            var denom = (long)targetBitrate * ntq;
            if (denom == 0) continue;

            var brpTimes = fClockHz / denom;
            if (brpTimes * denom != fClockHz) continue;

            var brp = (uint)brpTimes;
            if (brp < L.BrpMin || brp > L.BrpMax) continue;

            var tseg1Star = samplePoint * ntq - 1.0;
            var tseg1 = (uint)Math.Round(tseg1Star);
            tseg1 = Clamp<uint>(tseg1, (uint)L.Tseg1Min, Math.Min((uint)L.Tseg1Max, ntq - 2U)); // 预留至少 1 给 Sync、1 给 tseg2

            var tseg2 = ntq - tseg1 - 1U;
            if (tseg2 < L.Tseg2Min || tseg2 > L.Tseg2Max) continue;

            var sjw = fixedSjw ?? Math.Min(4, tseg2);
            if (sjw < L.SjwMin || sjw > L.SjwMax || sjw > tseg2) continue;

            var spActual = (1.0 + tseg1) / ntq;
            var err = Math.Abs(spActual - samplePoint);

            var take =
                err < bestErr ||
                (Math.Abs(err - bestErr) < 1e-12 &&
                 best.HasValue &&
                 L.PreferLargerNtqWhenTied &&
                 ntq > best.Value.Ntq);

            if (best is null || take)
            {
                var tqNs = 1e9 / (targetBitrate * (double)ntq);
                best = new BitTimingSegments(
                    brp, tseg1, tseg2, sjw
                );
                bestErr = err;
            }
        }

        if (best is null)
        {
            throw new InvalidOperationException(
                "找不到可行的时序（请调整 limits：NTQ 范围、TSEG 限制或允许近似分频）。");
        }

        return best.Value;
    }

    private static T Clamp<T>(this T value, T min, T max) where T : IComparable<T>
    {
        if (min.CompareTo(max) > 0)
            throw new ArgumentException("min 不能大于 max");

        if (value.CompareTo(min) < 0) return min;
        if (value.CompareTo(max) > 0) return max;
        return value;
    }
    /// <summary>
    /// CAN-FD 的快捷封装：分别为 Nominal/Data 求解。
    /// dataNtqMax 默认更小（高速相位常用更少的 NTQ）
    /// </summary>
    public static (BitTimingSegments Nominal, BitTimingSegments Data) FromSamplePointFd(
        uint fClockMHz,
        uint nomBitrate, double nomSamplePoint,
        uint dataBitrate, double dataSamplePoint,
        BitTimingLimits? nomLimits = null,
        BitTimingLimits? dataLimits = null
    )
    {
        nomLimits ??= new BitTimingLimits { NtqMin = 8, NtqMax = 40 };
        dataLimits ??= new BitTimingLimits { NtqMin = 8, NtqMax = 20 };

        var nom = FromSamplePoint(fClockMHz, nomBitrate, nomSamplePoint, nomLimits);
        var dat = FromSamplePoint(fClockMHz, dataBitrate, dataSamplePoint, dataLimits);
        return (nom, dat);
    }
}
