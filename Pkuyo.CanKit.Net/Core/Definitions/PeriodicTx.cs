using System;

namespace Pkuyo.CanKit.Net.Core.Definitions;

/// <summary>
/// Periodic transmit options.
/// 周期发送配置选项。
/// </summary>
public readonly record struct PeriodicTxOptions(
    TimeSpan Period,
    int Repeat = -1,
    bool FireImmediately = true);

