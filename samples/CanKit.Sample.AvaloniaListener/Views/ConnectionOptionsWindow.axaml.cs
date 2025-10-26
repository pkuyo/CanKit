using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class ConnectionOptionsWindow : Window
{
    public ConnectionOptionsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new ConnectionOptionsViewModel(new ConnectionOptionsContext(new MainViewModel()));
    }
    public ConnectionOptionsWindow(ConnectionOptionsViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (Design.IsDesignMode)
            return;

        Close(true);
    }
}
