using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CanKit.Core.Definitions;

namespace EndpointListenerWpf.Views
{
    public partial class SendFrameDialog : Window
    {
        public bool AllowFd { get; set; }

        public Func<ICanFrame, int>? Transmit { get; set; }

        public SendFrameDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Defaults
            IdTypeCombo.SelectedIndex = 0; // Standard
            FrameTypeCombo.SelectedIndex = AllowFd ? 1 : 0; // Default to FD if allowed

            // Disable FD option if not allowed
            if (!AllowFd)
            {
                ((ComboBoxItem)FrameTypeCombo.Items[1]).IsEnabled = false;
            }

            DlcBox.Text = AllowFd ? "8" : "8"; // default
            UpdateFlagVisibility();
        }

        private void OnFrameTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFlagVisibility();
        }

        private void UpdateFlagVisibility()
        {
            var isFd = string.Equals(((ComboBoxItem)FrameTypeCombo.SelectedItem).Tag?.ToString(), "CANFD", StringComparison.OrdinalIgnoreCase);
            if (RtrCheck != null) RtrCheck.Visibility = isFd ? Visibility.Collapsed : Visibility.Visible;
            if (BrsCheck != null) BrsCheck.Visibility = isFd ? Visibility.Visible : Visibility.Collapsed;
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
            // Replace commas/newlines with spaces, trim, then split by whitespace
            text = text.Replace(',', ' ');
            text = Regex.Replace(text, "\r?\n", " ");
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
        }

        private void OnSend(object sender, RoutedEventArgs e)
        {

            if (!TryParseInt(IdBox.Text, out var id) || id < 0)
            {
                MessageBox.Show(this, "Invalid ID.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseInt(DlcBox.Text, out var dlc) || dlc < 0)
            {
                MessageBox.Show(this, "Invalid DLC.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var isExtended = string.Equals(((ComboBoxItem)IdTypeCombo.SelectedItem).Tag?.ToString(), "Extend", StringComparison.OrdinalIgnoreCase);
            var isFd = string.Equals(((ComboBoxItem)FrameTypeCombo.SelectedItem).Tag?.ToString(), "CANFD", StringComparison.OrdinalIgnoreCase);

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

            try
            {
                if (isFd)
                {
                    // Map DLC to length for FD
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
                    var brs = BrsCheck?.IsChecked == true;
                    var frame = new CanFdFrame(id, bytes, isExtendedFrame: isExtended, BRS: brs, ESI: false);
                    var n = Transmit?.Invoke(frame);
                    if (n <= 0)
                        MessageBox.Show(this, "Frame not sent (driver rejected or not ready).", "Send", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    if (dlc > 8) { MessageBox.Show(this, "CAN 2.0 DLC must be 0..8.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                    var rtr = RtrCheck?.IsChecked == true;
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
                    var frame = new CanClassicFrame(id, bytes, isExtendedFrame: isExtended, isRemoteFrame: rtr);
                    var n = Transmit?.Invoke(frame);
                    if (n <= 0)
                        MessageBox.Show(this, "Frame not sent (driver rejected or not ready).", "Send", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to send: {ex.Message}", "Send", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
