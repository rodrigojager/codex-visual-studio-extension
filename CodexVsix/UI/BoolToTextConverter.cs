using System;
using System.Globalization;
using System.Windows.Data;
using CodexVsix.Services;

namespace CodexVsix.UI;

public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var localization = new LocalizationService();
        return value is true ? localization.RunningStatus : localization.ReadyStatus;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
