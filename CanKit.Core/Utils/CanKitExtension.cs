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
}
