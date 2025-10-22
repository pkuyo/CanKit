using System.ComponentModel;

namespace EndpointListenerWpf.Services
{
    public class AppBusState : IBusState
    {
        private bool _isListening;
        public bool IsListening
        {
            get => _isListening;
            private set
            {
                if (_isListening != value)
                {
                    _isListening = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsListening)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetListening(bool v) => IsListening = v;
    }
}
