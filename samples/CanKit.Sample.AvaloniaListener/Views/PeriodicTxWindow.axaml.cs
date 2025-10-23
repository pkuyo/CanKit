using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class PeriodicTxWindow : Window
{
    public PeriodicTxWindow(PeriodicViewModel vm)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = vm;
    }
}

