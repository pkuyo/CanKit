using System;

namespace CanKit.Core.Definitions
{
    /// <summary>
    /// Periodic transmit options. (周期发送配置选项。)
    /// </summary>
    public readonly record struct PeriodicTxOptions
    {
        /// <summary>
        /// Interval between consecutive transmissions. Must be greater than zero. (连续两次发送之间的时间间隔，必须大于 0。)
        /// </summary>
        public TimeSpan Period { get; init; }

        /// <summary>
        /// Number of repeats after the initial emission; use -1 for infinite repeats.
        ///
        /// (首次发送之后的重复次数；-1 表示无限重复。
        ///
        /// </summary>
        public int Repeat { get; init; }

        /// <summary>
        /// If true, emit once immediately upon scheduling, then follow the period;
        /// if false, the first emission is delayed .
        /// (为 true 时，安排后立即发送一次，然后按周期发送；
        /// 为 false 时，首次发送会被延后。
        /// </summary>
        public bool FireImmediately { get; init; }

        /// <summary>
        /// True if the schedule repeats indefinitely (Repeat &lt; 0). (若无限重复则为 true（Repeat &lt; 0）。)
        /// </summary>
        public bool IsInfinite => Repeat < 0;


        /// <summary>
        /// Create periodic transmit options. (创建周期发送配置。)
        /// </summary>
        /// <param name="period">Interval between sends; must be &gt; 0. (发送间隔；必须大于 0。)</param>
        /// <param name="repeat">Repeats after the first; -1 for infinite. (首次之后的重复次数；-1 表示无限。)</param>
        /// <param name="fireImmediately">Fire once immediately if true. (为 true 时安排后立即发送一次。)</param>
        public PeriodicTxOptions(
            TimeSpan period,
            int repeat = -1,
            bool fireImmediately = true)
        {
            if (period <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be greater than zero.");
            if (repeat < -1)
                throw new ArgumentOutOfRangeException(nameof(repeat), "Repeat must be -1 (infinite) or >= 0.");

            Period = period;
            Repeat = repeat;
            FireImmediately = fireImmediately;
        }
    }
}


