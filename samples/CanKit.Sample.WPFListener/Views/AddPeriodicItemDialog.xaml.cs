using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using EndpointListenerWpf.Models;
using CanKit.Core.Definitions;

namespace EndpointListenerWpf.Views
{
    public partial class AddPeriodicItemDialog : Window
    {
        public PeriodicItemModel? Result { get; private set; }
        public bool AllowFd { get; set; }

        public AddPeriodicItemDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            IdTypeCombo.SelectedIndex = 0; // Standard
            FrameTypeCombo.SelectedIndex = AllowFd ? 1 : 0; // prefer FD if allowed
            if (!AllowFd)
            {
                ((System.Windows.Controls.ComboBoxItem)FrameTypeCombo.Items[1]).IsEnabled = false;
            }
            DlcBox.Text = "8";
            PeriodBox.Text = "1000";
            UpdateFlagVisibility();
        }

        private void OnFrameTypeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateFlagVisibility();
        }

        private void UpdateFlagVisibility()
        {
            var isFd = string.Equals(((System.Windows.Controls.ComboBoxItem)FrameTypeCombo.SelectedItem).Tag?.ToString(), "CANFD", StringComparison.OrdinalIgnoreCase);
            RtrCheck.Visibility = isFd ? Visibility.Collapsed : Visibility.Visible;
            BrsCheck.Visibility = isFd ? Visibility.Visible : Visibility.Collapsed;
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

        private static byte[] ParseHexBytes(string text)
        {
            text = text ?? string.Empty;
            text = text.Replace(',', ' ');
            text = Regex.Replace(text, "\r?\n", " ");
            var parts = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!TryParseInt(IdBox.Text, out var id) || id < 0)
            {
                MessageBox.Show(this, "Invalid ID.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryParseInt(PeriodBox.Text, out var ms) || ms <= 0)
            {
                MessageBox.Show(this, "Invalid period (ms).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseInt(DlcBox.Text, out var dlc) || dlc < 0)
            {
                MessageBox.Show(this, "Invalid DLC.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var isExtended = string.Equals(((System.Windows.Controls.ComboBoxItem)IdTypeCombo.SelectedItem).Tag?.ToString(), "Extend", StringComparison.OrdinalIgnoreCase);
            var isFd = string.Equals(((System.Windows.Controls.ComboBoxItem)FrameTypeCombo.SelectedItem).Tag?.ToString(), "CANFD", StringComparison.OrdinalIgnoreCase);

            if (isFd && !AllowFd)
            {
                MessageBox.Show(this, "FD mode is not enabled.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = ParseHexBytes(DataBox.Text);
            }
            catch (Exception)
            {
                MessageBox.Show(this, "Invalid DATA. Use hex bytes like: 01 02 0A FF", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rtr = RtrCheck.IsChecked == true;
            var brs = BrsCheck.IsChecked == true;

            try
            {
                if (isFd)
                {
                    if (dlc > 15) { MessageBox.Show(this, "FD DLC must be 0..15.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    var targetLen = CanFdFrame.DlcToLen((byte)dlc);
                    if (bytes.Length > targetLen)
                    {
                        MessageBox.Show(this, $"DATA length ({bytes.Length}) exceeds FD DLC length ({targetLen}).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (bytes.Length < targetLen)
                    {
                        Array.Resize(ref bytes, targetLen); // pad zeros
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
                    if (dlc > 8) { MessageBox.Show(this, "CAN 2.0 DLC must be 0..8.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    if (rtr)
                    {
                        // For RTR, ignore provided data and use DLC only
                        bytes = new byte[dlc];
                    }
                    else
                    {
                        if (bytes.Length > dlc)
                        {
                            MessageBox.Show(this, $"DATA length ({bytes.Length}) exceeds DLC ({dlc}).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        if (bytes.Length < dlc)
                        {
                            Array.Resize(ref bytes, dlc); // pad zeros
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
                MessageBox.Show(this, $"Failed to build frame: {ex.Message}", "Validation", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
        }
    }
}
