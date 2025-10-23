using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class AddPeriodicItemDialog : Window
{
    public PeriodicItemModel? Result { get; private set; }
    public bool AllowFd { get; set; }
    private AddPeriodicItemDialogViewModel? _vm;

    public AddPeriodicItemDialog()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var vm = new AddPeriodicItemDialogViewModel
        {
            AllowFd = AllowFd
        };
        // Prefer FD if allowed
        vm.FrameTypeIndex = vm.AllowFd ? 1 : 0;
        vm.CloseRequested += (_, r) =>
        {
            Result = vm.Result;
            Close(r);
        };
        DataContext = _vm = vm;
    }
}
