using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class MainWindow : Window
{
    private PeriodicTxWindow? _periodicWindow;
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnOpenOptions(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (vm.Capabilities == null)
        {
            // No message box infra here; add a log entry instead
            vm.Logs.Add("[info] Load capabilities first (select endpoint).");
            return;
        }

        var dlg = new ConnectionOptionsWindow(vm);
        await dlg.ShowDialog(this);
    }

    private void OnOpenSend(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var dlg = new SendFrameDialog
        {
            AllowFd = vm.UseCanFd,
            Transmit = frame => vm.Transmit(frame)
        };
        dlg.Show(this);
    }

    private void OnOpenPeriodic(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (_periodicWindow == null)
        {
            _periodicWindow = new PeriodicTxWindow(vm.Periodic);
            _periodicWindow.Closed += (_, __) => _periodicWindow = null;

            vm.Periodic.AllowFd = vm.UseCanFd;
            vm.Periodic.ShowAddItemDialog = () =>
            {
                var dlg = new AddPeriodicItemDialog { AllowFd = vm.Periodic.AllowFd };
                var t = dlg.ShowDialog<bool?>(_periodicWindow);
                t.Wait();
                return t.Result == true ? dlg.Result : null;
            };

            _periodicWindow.Show(this);
            vm.Periodic.RefreshCanRun();
        }
        else
        {
            vm.Periodic.AllowFd = vm.UseCanFd;
            _periodicWindow.Activate();
            vm.Periodic.RefreshCanRun();
        }
    }
}
