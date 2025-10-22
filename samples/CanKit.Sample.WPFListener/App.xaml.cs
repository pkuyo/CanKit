using System.Configuration;
using System.Data;
using System.Windows;

namespace EndpointListenerWpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            if (System.String.Equals(culture.TwoLetterISOLanguageName, "zh", System.StringComparison.OrdinalIgnoreCase))
            {
                // Load Chinese resources to override defaults
                var zhDict = new ResourceDictionary { Source = new System.Uri("Resources/Strings.zh-CN.xaml", System.UriKind.Relative) };
                Resources.MergedDictionaries.Add(zhDict);
            }
        }
        catch
        {
            // Ignore localization failures and continue with defaults
        }
    }
}

