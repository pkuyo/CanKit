using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class EditFrameDialog : Window
{
    public bool AllowFd { get; set; }

    public Func<ICanFrame, int>? Transmit { get; set; }

    private ICanFrame? _initialFrame;

    public EditFrameDialog() { }

    public EditFrameDialog(ICanFrame? initial)
    {
        if (Design.IsDesignMode)
            return;
        AvaloniaXamlLoader.Load(this);
        if (initial != null)
            _initialFrame = initial;

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var vm = new EditFrameDialogViewModel
        {
            AllowFd = AllowFd,
            Transmit = Transmit
        };
        // Prefer FD if allowed
        vm.FrameTypeIndex = vm.AllowFd ? 1 : 0;
        vm.CloseRequested += (_, r) =>
        {
            if (Transmit == null && r == true)
            {
                // When used as picker, Close is triggered in OnConfirm
                return;
            }
            Close(r);
        };
        vm.OnConfirm = f => Close(f);

        if (_initialFrame != null)
        {
            // pre-fill
            var f = _initialFrame;
            vm.IsEdit = true;
            vm.FrameTypeIndex = f.FrameKind == CanFrameType.CanFd ? 1 : 0;
            vm.IdTypeIndex = f.IsExtendedFrame ? 1 : 0;
            vm.IdText = f.IsExtendedFrame ? $"0x{f.ID:X8}" : $"0x{f.ID:X3}";
            vm.DlcText = f.Dlc.ToString();
            vm.DataText = ToHex(f.Data.Span);
            if (f is CanClassicFrame c)
                vm.Rtr = c.IsRemoteFrame;
            if (f is CanFdFrame fd)
                vm.Brs = fd.BitRateSwitch;
        }
        DataContext = vm;
    }

    private static string ToHex(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0) return string.Empty;
        var sb = new StringBuilder(span.Length * 3);
        for (int i = 0; i < span.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(span[i].ToString("X2"));
        }
        return sb.ToString();
    }
}
