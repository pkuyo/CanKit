using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class AddPeriodicItemDialog : Window
{
    public PeriodicItemModel? Result { get; private set; }
    public bool AllowFd { get; set; }
    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }

    public AddPeriodicItemDialog()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        Opened += OnOpened;
        OkCommand = new RelayCommand(_ => OnOk(null, null));
        CancelCommand = new RelayCommand(_ => Close());
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var errorText = this.FindControl<TextBlock>("ErrorText");
        var idTypeCombo = this.FindControl<ComboBox>("IdTypeCombo");
        var frameTypeCombo = this.FindControl<ComboBox>("FrameTypeCombo");
        var dlcBox = this.FindControl<TextBox>("DlcBox");
        var periodBox = this.FindControl<TextBox>("PeriodBox");
        if (errorText != null) errorText.Text = string.Empty;
        if (idTypeCombo != null) idTypeCombo.SelectedIndex = 0; // Standard
        if (frameTypeCombo != null) frameTypeCombo.SelectedIndex = AllowFd ? 1 : 0; // prefer FD if allowed
        if (dlcBox != null) dlcBox.Text = "8";
        if (periodBox != null) periodBox.Text = "1000";
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

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var errorText = this.FindControl<TextBlock>("ErrorText");
        if (errorText != null) errorText.Text = string.Empty;

        var idBox = this.FindControl<TextBox>("IdBox");
        if (!TryParseInt(idBox?.Text, out var id) || id < 0)
        {
            if (errorText != null) errorText.Text = "Invalid ID.";
            return;
        }
        var periodBox = this.FindControl<TextBox>("PeriodBox");
        if (!TryParseInt(periodBox?.Text, out var ms) || ms <= 0)
        {
            if (errorText != null) errorText.Text = "Invalid period (ms).";
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

        var rtrCheck = this.FindControl<CheckBox>("RtrCheck");
        var brsCheck = this.FindControl<CheckBox>("BrsCheck");
        var rtr = rtrCheck?.IsChecked == true;
        var brs = brsCheck?.IsChecked == true;

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
                Result = new PeriodicItemModel
                {
                    Enabled = true,
                    Id = id,
                    PeriodMs = ms,
                    IsFd = true,
                    IsExtended = isExtended,
                    Brs = brs,
                    IsRemote = false,
                    DataBytes = bytes,
                    Dlc = (byte)dlc,
                };
            }
            else
            {
                if (dlc > 8)
                {
                    if (errorText != null) errorText.Text = "CAN 2.0 DLC must be 0..8.";
                    return;
                }
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
                Result = new PeriodicItemModel
                {
                    Enabled = true,
                    Id = id,
                    PeriodMs = ms,
                    IsFd = false,
                    IsExtended = isExtended,
                    IsRemote = rtr,
                    Brs = false,
                    DataBytes = bytes,
                    Dlc = (byte)dlc,
                };
            }
        }
        catch (Exception ex)
        {
            if (errorText != null) errorText.Text = $"Failed to build frame: {ex.Message}";
            return;
        }

        Close(true);
    }
}
