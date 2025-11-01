using System;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;

namespace CanKit.Abstractions.API.Common;

public interface IFilterRule
{
    Func<CanFrame, bool> Build();

    CanFilterIDType FilterIdType { get; }
}
