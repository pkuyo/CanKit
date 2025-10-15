using CanKit.Core.Definitions;
using CanKit.Core.Exceptions;
using Kvaser.CanLib;

namespace CanKit.Adapter.Kvaser.Utils;

public static class KvaserUtils
{
    public static void ThrowIfError(Canlib.canStatus status, string operation, string message)
    {
        if (status != Canlib.canStatus.canOK)
            throw new Exceptions.KvaserCanException(operation, message, status);
    }

    public static int MapToKvaserConst(int bitrate)
    {
        switch (bitrate)
        {
            case 1_000_000: return Canlib.canBITRATE_1M;
            case 500_000: return Canlib.canBITRATE_500K;
            case 250_000: return Canlib.canBITRATE_250K;
            case 125_000: return Canlib.canBITRATE_125K;
            case 100_000: return Canlib.canBITRATE_100K;
            case 83_333:
            case 83_334: return Canlib.canBITRATE_83K;
            case 62_500:
            case 62_000: return Canlib.canBITRATE_62K;
            case 50_000: return Canlib.canBITRATE_50K;
            case 10_000: return Canlib.canBITRATE_10K;
            default:
                throw new CanBusConfigurationException(
                    $"Unsupported classic bitrate: {bitrate} bps for Kvaser predefined constants.");
        }
    }


    public static int MapToKvaserFdConst(int bitrate, int samplePointPercent = 80)
    {
        var sp = NormalizeSp(samplePointPercent);

        var c = LookupFdConst(bitrate, sp);
        if (c.HasValue)
        {
            return c.Value;
        }

        foreach (var alt in FallbackOrder(sp))
        {
            c = LookupFdConst(bitrate, alt);
            if (c.HasValue) return c.Value;
        }

        // 未找到预定义常量：回退为原始 bps
        throw new CanBusConfigurationException(
            $"Unsupported Kvaser FD bitrate/sample-point combination: {bitrate} bps @ {sp}%.");

        // —— 内部小工具 —— //
        static int NormalizeSp(int s)
            => (s >= 75 && s < 85) ? 80 : (s >= 65 && s < 75) ? 70 : 60;

        static IEnumerable<int> FallbackOrder(int s)
        {
            // 以 80% 为首选，其它为候选
            if (s == 80)
            {
                yield return 70;
                yield return 60;
            }
            else if (s == 70)
            {
                yield return 80;
                yield return 60;
            }
            else
            {
                yield return 80;
                yield return 70;
            }
        }

        static int? LookupFdConst(int bps, int sp)
        {
            switch (bps)
            {
                case 500_000:
                    return (sp == 80) ? Canlib.canFD_BITRATE_500K_80P : null;

                case 1_000_000:
                    return (sp == 80) ? Canlib.canFD_BITRATE_1M_80P : null;

                case 2_000_000:
                    if (sp == 80) return Canlib.canFD_BITRATE_2M_80P;
                    if (sp == 60) return Canlib.canFD_BITRATE_2M_60P;
                    return null;

                case 4_000_000:
                    return (sp == 80) ? Canlib.canFD_BITRATE_4M_80P : null;

                case 8_000_000:
                    if (sp == 80) return Canlib.canFD_BITRATE_8M_80P;
                    if (sp == 60) return Canlib.canFD_BITRATE_8M_60P;
                    return null;

                default:
                    return null;
            }
        }
    }

    public static int KvaserHandle(this BusNativeHandle handle) => (int)handle.HandleValue;
}
