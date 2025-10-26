using System.ComponentModel;

namespace CanKit.Sample.AvaloniaListener.Abstractions
{
    public interface IBusState : INotifyPropertyChanged
    {
        bool IsListening { get; }
    }
}

