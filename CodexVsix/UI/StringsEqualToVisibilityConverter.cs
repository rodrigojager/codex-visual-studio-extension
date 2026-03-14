using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CodexVsix.UI;

public sealed class StringsEqualToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var left = values.Length > 0 ? values[0]?.ToString() ?? string.Empty : string.Empty;
        var right = values.Length > 1 ? values[1]?.ToString() ?? string.Empty : string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
