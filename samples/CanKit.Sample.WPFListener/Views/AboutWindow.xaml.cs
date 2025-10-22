using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace EndpointListenerWpf.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            string version = "";
            try
            {
                var entry = Assembly.GetEntryAssembly();
                if (entry != null)
                {
                    var fvi = FileVersionInfo.GetVersionInfo(entry.Location);
                    version = fvi.ProductVersion ?? entry.GetName().Version?.ToString() ?? "";
                }
            }
            catch { }
            VersionText.Text = version;
        }

        private void OnNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = e.Uri.ToString(),
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch { }
            e.Handled = true;
        }
    }
}

