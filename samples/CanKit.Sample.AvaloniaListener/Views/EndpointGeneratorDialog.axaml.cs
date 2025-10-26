using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class EndpointGeneratorDialog : Window
{
    public EndpointGeneratorDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new EndpointGeneratorViewModel();
    }

    private async void OnGenerate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not EndpointGeneratorViewModel vm) { Close(null); return; }
        var text = vm.Preview;
        if (string.IsNullOrWhiteSpace(text))
        {
            Close(null);
            return;
        }

        try
        {
            var app = Application.Current;
            if (app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } w)
            {
                await w.Clipboard!.SetTextAsync(text);
            }
        }
        catch { }

        Close(text);
    }
}

