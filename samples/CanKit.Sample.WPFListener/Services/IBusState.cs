using System.ComponentModel;

namespace EndpointListenerWpf.Services
{
    public interface IBusState : INotifyPropertyChanged
    {
        bool IsListening { get; }
    }
}

