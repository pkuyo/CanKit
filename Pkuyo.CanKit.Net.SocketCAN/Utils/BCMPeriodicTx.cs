using Pkuyo.CanKit.Net.Core.Abstractions;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.SocketCAN.Native;

namespace Pkuyo.CanKit.Net.SocketCAN.Utils;

public class BCMPeriodicTx : IPeriodicTx
{

    public BCMPeriodicTx(SocketCanBusRtConfigurator configurator)
    {
        _fd = Libc.socket(Libc.AF_CAN, Libc.SOCK_DGRAM, Libc.CAN_BCM);
        if (_fd < 0)
        {
            //TODO:异常处理
        }
        var ifr = new Libc.ifreq { ifr_name = configurator.InterfaceName };


    }

    public void Update(CanTransmitData? frame = null, TimeSpan? period = null, int? remainingCount = null) => throw new NotImplementedException();

    public void Dispose()
    {
        // TODO 在此释放托管资源
    }

    public bool IsRunning { get; }
    public TimeSpan Period { get; }
    public int RemainingCount { get; }
    public void Stop() => throw new NotImplementedException();



    public event EventHandler? Completed;

    private readonly int _fd;
}
