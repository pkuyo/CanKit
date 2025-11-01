using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core.Definitions;

namespace CanKit.Adapter.Vector.Transceivers;

public interface IVectorTransceiver : ITransceiver
{
    bool ReceiveEvents(VectorBus bus, List<CanReceiveData> frames, List<ICanErrorInfo> errorInfos);
}
