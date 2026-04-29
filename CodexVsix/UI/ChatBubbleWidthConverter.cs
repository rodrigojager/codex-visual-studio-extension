using System;
using System.Globalization;
using System.Windows.Data;

namespace CodexVsix.UI;

public sealed class ChatBubbleWidthConverter : IValueConverter
{
    private const double BubbleWidthRatio = 0.85d;
    private const double MinimumBubbleWidth = 24d;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double actualWidth || double.IsNaN(actualWidth) || actualWidth <= 0)
        {
            return 360d * BubbleWidthRatio;
        }

        return Math.Max(MinimumBubbleWidth, actualWidth * BubbleWidthRatio);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
