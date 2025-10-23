using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class SendFrameDialog : Window
{
    public bool AllowFd { get; set; }

    public Func<ICanFrame, int>? Transmit { get; set; }

    public SendFrameDialog()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var vm = new SendFrameDialogViewModel
        {
            AllowFd = AllowFd,
            Transmit = Transmit
        };
        // Prefer FD if allowed
        vm.FrameTypeIndex = vm.AllowFd ? 1 : 0;
        vm.CloseRequested += (_, r) => Close(r);
        DataContext = vm;
    }
}
