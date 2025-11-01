using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Common;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Abstractions.SPI;
using CanKit.Abstractions.SPI.Common;
using CanKit.Core.Definitions;
using CanKit.Core.Utils;

namespace CanKit.Protocol.IsoTp.Options;

public class IsoTpOptions : IBusOptions
{
    public CanFeature Features { get; set; }
    public int ChannelIndex { get; set; }
    public string? ChannelName { get; set; }
    public CanBusTiming BitTiming { get; set; }
    public bool InternalResistance { get; set; }
    public CanProtocolMode ProtocolMode { get; set; }
    public CanFeature EnabledSoftwareFallback { get; set; } = CanFeature.All;
    public IBufferAllocator BufferAllocator { get; set; } = new ArrayPoolBufferAllocator();


    /* ----IsoTpSettings----- */

    public bool CanPadding { get; set; } = true;
    public TimeSpan? GlobalBusGuard { get; set; } = null;   // 数据帧最小全局间隔（FC不受限）
    public bool N_AxCheck { get; set; } = false;
    public QueuedCanBusOptions? QueuedCanBusOptions { get; set; } = null;

    public TimeSpan N_As { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan N_Ar { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan N_Bs { get; set; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan N_Br { get; set; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan N_Cs { get; set; } = TimeSpan.FromMilliseconds(1000);
    public TimeSpan N_Cr { get; set; } = TimeSpan.FromMilliseconds(1000);

    public int MaxFrameLength { get; set; } = int.MaxValue;

    #region Ignored config

    public ChannelWorkMode WorkMode { get; set; }
    public TxRetryPolicy TxRetryPolicy { get; set; }
    public ICanFilter Filter { get; set; } = new CanFilter();
    public Capability Capabilities { get; set; } = new(CanFeature.All);
    public bool AllowErrorInfo { get; set; }
    public int AsyncBufferCapacity { get; set; }

    #endregion
}
