using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CodexVsix.UI;

public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Brushes.OrangeRed : Brushes.SeaGreen;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
