using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media.Animation;

namespace EndpointListenerWpf.Views
{
    public partial class ZlgEndpointBuilderWindow : Window
    {
        public List<DeviceTypeItem> DeviceTypes { get; } = new()
        {
            new("USBCAN-II", "USBCAN2"),
            new("USBCAN-I", "USBCAN1"),
            new("USBCAN-E-U", "USBCAN-E-U"),
            new("USBCAN-2E-U", "USBCAN-2E-U"),
            new("USBCAN-4E-U", "USBCAN-4E-U"),
            new("USBCAN-8E-U", "USBCAN-8E-U"),
            new("USBCANFD-100U", "USBCANFD-100U"),
            new("USBCANFD-200U", "USBCANFD-200U"),
            new("USBCANFD-MINI", "USBCANFD-MINI"),
            new("PCIE-CANFD-200U", "PCIE-CANFD-200U"),
            new("PCIE-CANFD-400U", "PCIE-CANFD-400U"),
            new("PCIE9820", "PCIE9820"),
            new("PCIE9820I", "PCIE9820I"),
        };

        public ZlgEndpointBuilderWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private static bool TryParseUInt(string text, out uint value)
        {
            text = (text ?? string.Empty).Trim();
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private void OnGenerateCopy(object sender, RoutedEventArgs e)
        {
            if (DeviceTypeCombo?.SelectedItem is not DeviceTypeItem item)
            {
                return;
            }
            if (!TryParseUInt(DeviceIndexBox?.Text ?? "", out var devIdx))
            {
                devIdx = 0;
            }
            if (!TryParseUInt(ChannelIndexBox?.Text ?? "", out var chIdx))
            {
                chIdx = 0;
            }

            // Build endpoint: use path without ZCAN_ prefix; ZlgEndpoint resolves it
            var path = item.Path;
            var ep = $"zlg://{path}?index={devIdx.ToString(CultureInfo.InvariantCulture)}#ch{chIdx.ToString(CultureInfo.InvariantCulture)}";
            EndpointBox.Text = ep;
            try
            {
                System.Windows.Clipboard.SetText(ep);
                ShowToast();
            }
            catch { }
        }

        private void ShowToast()
        {
            if (Toast == null) return;
            Toast.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(120)))
            {
                FillBehavior = FillBehavior.Stop
            };
            fadeIn.Completed += (_, __) => Toast.Opacity = 1;
            Toast.BeginAnimation(OpacityProperty, fadeIn);

            var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(500)))
            {
                BeginTime = TimeSpan.FromMilliseconds(1200),
                FillBehavior = FillBehavior.Stop
            };
            fadeOut.Completed += (_, __) =>
            {
                Toast.Opacity = 0;
                Toast.Visibility = Visibility.Collapsed;
            };
            Toast.BeginAnimation(OpacityProperty, fadeOut);
        }

        public record DeviceTypeItem(string DisplayName, string Path)
        {
            public override string ToString() => DisplayName;
        }
    }
}

