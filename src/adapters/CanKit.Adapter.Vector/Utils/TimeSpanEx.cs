namespace CanKit.Adapter.Vector.Utils;

internal static class TimeSpanEx
{
    public static TimeSpan FromNanoseconds(ulong ns)
    {
        ulong ticksU = (ns + 50UL) / 100UL;
        if (ticksU > (ulong)TimeSpan.MaxValue.Ticks)
            return TimeSpan.MaxValue;

        return TimeSpan.FromTicks((long)ticksU);
    }
}
