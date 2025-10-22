using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CanKit.Core.Definitions;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Views
{
    public partial class AddFilterDialog : Window
    {
        public FilterRuleModel? Result { get; private set; }

        public AddFilterDialog()
        {
            InitializeComponent();
            TypeCombo.SelectedIndex = 0; // Mask
            IdTypeCombo.SelectedIndex = 0; // Standard
        }

        private static bool TryParseInt(string text, out int value)
        {
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var kind = ((ComboBoxItem)TypeCombo.SelectedItem).Content?.ToString();
            var idTypeStr = ((ComboBoxItem)IdTypeCombo.SelectedItem).Content?.ToString();
            var idType = idTypeStr?.Equals("Extend", StringComparison.OrdinalIgnoreCase) == true
                ? CanFilterIDType.Extend
                : CanFilterIDType.Standard;

            if (!TryParseInt(FirstBox.Text, out var first))
            {
                MessageBox.Show(this, "Invalid first value.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseInt(SecondBox.Text, out var second))
            {
                MessageBox.Show(this, "Invalid second value.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show(this, "Range: From must be <= To.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            DialogResult = true;
        }
    }
}
