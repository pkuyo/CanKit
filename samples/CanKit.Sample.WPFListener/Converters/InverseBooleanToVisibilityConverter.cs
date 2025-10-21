using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EndpointListenerWpf.Converters;

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is bool and true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type t, object? p, CultureInfo c)
        => value is Visibility v && v != Visibility.Visible;
}


