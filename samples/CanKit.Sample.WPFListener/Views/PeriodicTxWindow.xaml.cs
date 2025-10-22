using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using EndpointListenerWpf.ViewModels;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.Views
{
    public partial class PeriodicTxWindow : Window
    {
        private PeriodicViewModel _viewModel { get; }


        public PeriodicTxWindow(PeriodicViewModel dataContext)
        {
            InitializeComponent();
            DataContext = _viewModel = dataContext;

        }

        private void OnRowRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_viewModel.IsRunning) return; // disable deletion while running
            if (sender is DataGridRow row && row.DataContext is PeriodicItemModel item)
            {
                var menu = new ContextMenu();
                var mi = new MenuItem { Header = "Delete" };
                mi.Click += (_, _) => { _viewModel.Items.Remove(item); };
                menu.Items.Add(mi);
                menu.IsOpen = true;
            }
        }
    }
}
