using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CanKit.Sample.AvaloniaListener.Converters;

public class EnumToBooleanConverter : IValueConverter
{
    public static readonly EnumToBooleanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;
        var paramStr = parameter.ToString();
        if (paramStr == null)
            return false;
        return string.Equals(value.ToString(), paramStr, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter == null)
            return null;
        var paramStr = parameter.ToString();
        if (paramStr == null)
            return null;
        // Return the enum value by parsing the parameter
        try
        {
            return Enum.Parse(targetType, paramStr);
        }
        catch
        {
            return null;
        }
    }
}

