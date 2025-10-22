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
        public bool SupportsListenOnly { get; set; } = false;
        public bool SupportsErrorCounters { get; set; } = false;

        public bool UseCan20 { get; set; } = true;
        public bool UseCanFd { get; set; } = false;
        public bool ListenOnly { get; set; } = false;

        public ObservableCollection<int> BitRates { get; set; } = new();
        public ObservableCollection<int> DataBitRates { get; set; } = new();

        public int SelectedBitRate { get; set; }
        public int SelectedDataBitRate { get; set; }

        public int ErrorCountersPeriodMs { get; set; } = 5000;

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

            // Listen-only support
            ListenOnlyCheck.Visibility = SupportsListenOnly ? Visibility.Visible : Visibility.Collapsed;
            ListenOnlyCheck.IsChecked = ListenOnly;

            // Error counters poll config
            var showCounters = SupportsErrorCounters;
            ErrorCounterLabel.Visibility = showCounters ? Visibility.Visible : Visibility.Collapsed;
            ErrorCounterPeriodBox.Visibility = showCounters ? Visibility.Visible : Visibility.Collapsed;
            ErrorCounterUnit.Visibility = showCounters ? Visibility.Visible : Visibility.Collapsed;
            ErrorCounterPeriodBox.Text = ErrorCountersPeriodMs.ToString();
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
            if (ListenOnlyCheck.Visibility == Visibility.Visible)
                ListenOnly = ListenOnlyCheck.IsChecked == true;
            if (ErrorCounterPeriodBox.Visibility == Visibility.Visible)
            {
                if (int.TryParse(ErrorCounterPeriodBox.Text.Trim(), out var v))
                {
                    ErrorCountersPeriodMs = Math.Max(100, v);
                }
            }
            DialogResult = true;
        }
    }
}
