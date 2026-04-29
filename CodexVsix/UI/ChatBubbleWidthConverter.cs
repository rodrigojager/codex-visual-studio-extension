using System;
using System.Globalization;
using System.Windows.Data;

namespace CodexVsix.UI;

public sealed class ChatBubbleWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double actualWidth || double.IsNaN(actualWidth) || actualWidth <= 0)
        {
            return 360d;
        }

        // Use most of the chat viewport while leaving a clear visual gutter for alignment.
        var availableWidth = Math.Max(120d, actualWidth - 28d);
        return Math.Max(120d, availableWidth * 0.85d);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
