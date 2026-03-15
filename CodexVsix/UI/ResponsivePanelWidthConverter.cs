using System;
using System.Globalization;
using System.Windows.Data;

namespace CodexVsix.UI;

public sealed class ResponsivePanelWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double actualWidth || double.IsNaN(actualWidth) || actualWidth <= 0)
        {
            return 320d;
        }

        // Keep the floating panel close to full width without colliding with the outer margins.
        var computedWidth = actualWidth - 48d;
        return Math.Max(320d, Math.Min(computedWidth, 820d));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
