using System.ComponentModel;

namespace CanKit.Sample.AvaloniaListener.Services
{
    public interface IBusState : INotifyPropertyChanged
    {
        bool IsListening { get; }
    }
}

