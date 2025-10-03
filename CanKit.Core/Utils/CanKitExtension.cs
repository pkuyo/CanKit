using System;
using System.Collections.Generic;
using System.Linq;
using CanKit.Core.Definitions;

namespace CanKit.Core.Utils;

public static class CanKitExtension
{
    /// <summary>
    /// Apply filter rules to a stream of received data.
    /// 根据规则对接收数据进行过滤。
    /// </summary>
    /// <param name="filter">Filter holder</param>
    /// <param name="inData">Input data</param>
    /// <param name="useSoftware">Use software rules instead of hardware rules</param>
    /// <returns>Filtered sequence</returns>
    public static IEnumerable<CanReceiveData> FilterData(CanFilter filter, IEnumerable<CanReceiveData> inData, bool useSoftware = false)
    {
        var rules = useSoftware ? filter.SoftwareFilterRules : filter.FilterRules;
        if (rules.Count == 0)
            return inData;
        var predicate = FilterRule.Build(rules);
        return inData.Where(d => predicate(d.CanFrame));
    }

    /// <summary>
    /// 获取错误帧原始数据区（来自 ICanFrame.Data），为空则返回 Empty。
    /// </summary>
    public static ReadOnlySpan<byte> RawData(this ICanErrorInfo e)
        => (e?.Frame) is { } f ? f.Data.Span : ReadOnlySpan<byte>.Empty;

    public static CanControllerStatus ToControllerStatus(int rec, int tec,
        int warningLimit = 96, int passiveLimit = 128)
    {
        var s = CanControllerStatus.None;
        if (rec == 0 && tec == 0) return s;

        if (rec >= passiveLimit) s |= CanControllerStatus.RxPassive;
        else if (rec >= warningLimit) s |= CanControllerStatus.RxWarning;

        if (tec >= passiveLimit) s |= CanControllerStatus.TxPassive;
        else if (tec >= warningLimit) s |= CanControllerStatus.TxWarning;

        // 两个计数都低于告警阈值 → 视为 Error Active
        if (rec < warningLimit && tec < warningLimit)
            s |= CanControllerStatus.Active;

        return s;
    }
}
