using System;
using System.Globalization;
using System.Windows.Data;

namespace CodexVsix.UI;

public sealed class RecentHistoryMaxHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4
            || values[0] is not double contentHeight
            || double.IsNaN(contentHeight)
            || contentHeight <= 0
            || values[1] is not double composerHeight
            || double.IsNaN(composerHeight)
            || composerHeight < 0
            || values[2] is not double toolbarHeight
            || double.IsNaN(toolbarHeight)
            || toolbarHeight < 0
            || values[3] is not bool isExpanded
            || !isExpanded)
        {
            return double.PositiveInfinity;
        }

        // The history card lives between the top toolbar and the composer.
        // Reserve card chrome + a visual gap so it never touches or slips under the composer.
        var availableBetweenToolbarAndComposer = contentHeight - composerHeight - toolbarHeight - 20d;
        var maxListHeight = availableBetweenToolbarAndComposer - 108d;
        return Math.Max(140d, maxListHeight);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
