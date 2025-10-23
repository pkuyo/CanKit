using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class AddFilterDialog : Window
{
    public FilterRuleModel? Result { get; private set; }

    public AddFilterDialog()
    {
        AvaloniaXamlLoader.Load(this);
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

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (Design.IsDesignMode)
            return;
        var errorText = this.FindControl<TextBlock>("ErrorText");
        var typeCombo = this.FindControl<ComboBox>("TypeCombo");
        var idTypeCombo = this.FindControl<ComboBox>("IdTypeCombo");
        var firstBox = this.FindControl<TextBox>("FirstBox");
        var secondBox = this.FindControl<TextBox>("SecondBox");
        if (errorText != null) errorText.Text = string.Empty;
        var kindItem = typeCombo?.SelectedItem as ComboBoxItem;
        var idTypeItem = idTypeCombo?.SelectedItem as ComboBoxItem;
        var kind = kindItem?.Content?.ToString() ?? "Mask";
        var idTypeStr = idTypeItem?.Content?.ToString();
        var idType = string.Equals(idTypeStr, "Extend", StringComparison.OrdinalIgnoreCase)
            ? CanFilterIDType.Extend
            : CanFilterIDType.Standard;

        if (!TryParseInt(firstBox?.Text, out var first))
        {
            if (errorText != null) errorText.Text = "Invalid first value.";
            return;
        }

        if (!TryParseInt(secondBox?.Text, out var second))
        {
            if (errorText != null) errorText.Text = "Invalid second value.";
            return;
        }

        if (string.Equals(kind, "Mask", StringComparison.OrdinalIgnoreCase))
        {
            Result = new FilterRuleModel
            {
                Kind = FilterKind.Mask,
                IdType = idType,
                AccCode = first,
                AccMask = second
            };
        }
        else
        {
            if (first > second)
            {
                if (errorText != null) errorText.Text = "Range: From must be <= To.";
                return;
            }
            Result = new FilterRuleModel
            {
                Kind = FilterKind.Range,
                IdType = idType,
                From = first,
                To = second
            };
        }

        Close(true);
    }
}
