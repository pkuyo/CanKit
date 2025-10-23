using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class SendFrameDialog : Window
{
    public bool AllowFd { get; set; }

    public Func<ICanFrame, int>? Transmit { get; set; }

    public RelayCommand SendCommand { get; }
    public RelayCommand CloseCommand { get; }

    public SendFrameDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        Opened += OnOpened;
        SendCommand = new RelayCommand(_ => OnSend(null, null));
        CloseCommand = new RelayCommand(_ => Close());
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var errorText = this.FindControl<TextBlock>("ErrorText");
        var idTypeCombo = this.FindControl<ComboBox>("IdTypeCombo");
        var frameTypeCombo = this.FindControl<ComboBox>("FrameTypeCombo");
        var dlcBox = this.FindControl<TextBox>("DlcBox");
        if (errorText != null) errorText.Text = string.Empty;
        if (idTypeCombo != null) idTypeCombo.SelectedIndex = 0; // Standard
        if (frameTypeCombo != null) frameTypeCombo.SelectedIndex = AllowFd ? 1 : 0; // prefer FD if allowed
        if (dlcBox != null) dlcBox.Text = "8";
        UpdateFlagVisibility();
    }

    private void OnFrameTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateFlagVisibility();
    }

    private void UpdateFlagVisibility()
    {
        var frameTypeCombo = this.FindControl<ComboBox>("FrameTypeCombo");
        var typeItem = frameTypeCombo?.SelectedItem as ComboBoxItem;
        var isFd = string.Equals(typeItem?.Content?.ToString(), "CANFD", StringComparison.OrdinalIgnoreCase);
        var rtr = this.FindControl<CheckBox>("RtrCheck");
        var brs = this.FindControl<CheckBox>("BrsCheck");
        if (rtr != null) rtr.IsVisible = !isFd;
        if (brs != null) brs.IsVisible = isFd;
    }

    private static bool TryParseInt(string? text, out int value)
    {
        text = (text ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static byte[] ParseHexBytes(string? text)
    {
        text = text ?? string.Empty;
        text = text.Replace(',', ' ');
        text = Regex.Replace(text, "\r?\n", " ");
        var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
    }

    private void OnSend(object? sender, RoutedEventArgs e)
    {
        var errorText = this.FindControl<TextBlock>("ErrorText");
        if (errorText != null) errorText.Text = string.Empty;

        var idBox = this.FindControl<TextBox>("IdBox");
        if (!TryParseInt(idBox?.Text, out var id) || id < 0)
        {
            if (errorText != null) errorText.Text = "Invalid ID.";
            return;
        }

        var dlcBox = this.FindControl<TextBox>("DlcBox");
        if (!TryParseInt(dlcBox?.Text, out var dlc) || dlc < 0)
        {
            if (errorText != null) errorText.Text = "Invalid DLC.";
            return;
        }

        var idTypeCombo = this.FindControl<ComboBox>("IdTypeCombo");
        var frameTypeCombo = this.FindControl<ComboBox>("FrameTypeCombo");
        var idTypeItem = idTypeCombo?.SelectedItem as ComboBoxItem;
        var frameTypeItem = frameTypeCombo?.SelectedItem as ComboBoxItem;
        var isExtended = string.Equals(idTypeItem?.Content?.ToString(), "Extend", StringComparison.OrdinalIgnoreCase);
        var isFd = string.Equals(frameTypeItem?.Content?.ToString(), "CANFD", StringComparison.OrdinalIgnoreCase);

        if (isFd && !AllowFd)
        {
            if (errorText != null) errorText.Text = "FD mode is not enabled.";
            return;
        }

        byte[] bytes;
        try
        {
            var dataBox = this.FindControl<TextBox>("DataBox");
            bytes = ParseHexBytes(dataBox?.Text);
        }
        catch
        {
            if (errorText != null) errorText.Text = "Invalid DATA. Use hex bytes like: 01 02 0A FF";
            return;
        }

        try
        {
            if (isFd)
            {
                if (dlc > 15)
                {
                    if (errorText != null) errorText.Text = "FD DLC must be 0..15.";
                    return;
                }
                var targetLen = CanFdFrame.DlcToLen((byte)dlc);
                if (bytes.Length > targetLen)
                {
                    if (errorText != null) errorText.Text = $"DATA length ({bytes.Length}) exceeds FD DLC length ({targetLen}).";
                    return;
                }
                if (bytes.Length < targetLen)
                {
                    Array.Resize(ref bytes, targetLen);
                }
                var brsCheck = this.FindControl<CheckBox>("BrsCheck");
                var brs = brsCheck?.IsChecked == true;
                var frame = new CanFdFrame(id, bytes, isExtendedFrame: isExtended, BRS: brs, ESI: false);
                var n = Transmit?.Invoke(frame) ?? 0;
                if (n <= 0)
                    if (errorText != null) errorText.Text = "Frame not sent (driver rejected or not ready).";
            }
            else
            {
                if (dlc > 8)
                {
                    if (errorText != null) errorText.Text = "CAN 2.0 DLC must be 0..8.";
                    return;
                }
                var rtrCheck = this.FindControl<CheckBox>("RtrCheck");
                var rtr = rtrCheck?.IsChecked == true;
                if (rtr)
                {
                    bytes = new byte[dlc];
                }
                else
                {
                    if (bytes.Length > dlc)
                    {
                        if (errorText != null) errorText.Text = $"DATA length ({bytes.Length}) exceeds DLC ({dlc}).";
                        return;
                    }
                    if (bytes.Length < dlc)
                    {
                        Array.Resize(ref bytes, dlc);
                    }
                }
                var frame = new CanClassicFrame(id, bytes, isExtendedFrame: isExtended, isRemoteFrame: rtr);
                var n = Transmit?.Invoke(frame) ?? 0;
                if (n <= 0)
                    if (errorText != null) errorText.Text = "Frame not sent (driver rejected or not ready).";
            }
        }
        catch (Exception ex)
        {
            if (errorText != null) errorText.Text = $"Failed to send: {ex.Message}";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
