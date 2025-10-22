using System.Configuration;
using System.Data;
using System.Windows;

namespace EndpointListenerWpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            if (string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
            {
                var zhDict = new ResourceDictionary { Source = new Uri("Resources/Strings.zh-CN.xaml", System.UriKind.Relative) };
                Resources.MergedDictionaries.Add(zhDict);
            }
        }
        catch
        {
            // Ignore localization failures and continue with defaults
        }
    }
}

