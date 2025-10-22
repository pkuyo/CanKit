using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using EndpointListenerWpf.Models;
using EndpointListenerWpf.ViewModels;

namespace EndpointListenerWpf.Views
{
    public partial class ConnectionOptionsDialog : Window
    {
        public bool SupportsCan20 { get; set; } = true;
        public bool SupportsCanFd { get; set; } = false;

        public bool UseCan20 { get; set; } = true;
        public bool UseCanFd { get; set; } = false;

        public ObservableCollection<int> BitRates { get; set; } = new();
        public ObservableCollection<int> DataBitRates { get; set; } = new();

        public int SelectedBitRate { get; set; }
        public int SelectedDataBitRate { get; set; }

        public ObservableCollection<FilterRuleModel> Filters { get; set; } = new();

        public ConnectionOptionsDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Setup radio state and availability
            Can20Radio.IsEnabled = SupportsCan20;
            CanFdRadio.IsEnabled = SupportsCanFd;

            // Select radio based on current flags
            if (UseCanFd && SupportsCanFd)
            {
                CanFdRadio.IsChecked = true;
            }
            else
            {
                Can20Radio.IsChecked = true;
            }

            // Bind combos
            BitrateCombo.ItemsSource = BitRates;
            DataBitrateCombo.ItemsSource = DataBitRates;
            BitrateCombo.SelectedItem = SelectedBitRate;
            DataBitrateCombo.SelectedItem = SelectedDataBitRate;

            // Data bitrate controls visibility depends on FD mode
            UpdateFdVisibility();

            Can20Radio.Checked += (_, _) => { UseCan20 = true; UseCanFd = false; UpdateFdVisibility(); };
            CanFdRadio.Checked += (_, _) => { UseCan20 = false; UseCanFd = true; UpdateFdVisibility(); };
        }

        private void UpdateFdVisibility()
        {
            var vis = UseCanFd ? Visibility.Visible : Visibility.Collapsed;
            DataBitrateCombo.Visibility = vis;
            DataBitrateLabel.Visibility = vis;
        }

        private void OnEditFilters(object sender, RoutedEventArgs e)
        {
            var win = new FilterEditorWindow
            {
                Owner = this
            };
            win.DataContext = new FilterEditorViewModel(Filters);
            win.ShowDialog();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            // Capture selections
            if (BitrateCombo.SelectedItem is int br)
                SelectedBitRate = br;
            if (DataBitrateCombo.SelectedItem is int dbr)
                SelectedDataBitRate = dbr;
            DialogResult = true;
        }
    }
}
