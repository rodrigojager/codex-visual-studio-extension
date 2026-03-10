using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace CodexVsix.UI;

public sealed class PathToFileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Path.GetFileName(text);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
