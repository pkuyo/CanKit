using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class DeviceManagerWindow : Window
{
    public DeviceManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public DeviceManagerWindow(MainViewModel main)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new DeviceManagerViewModel(main);
    }
}

