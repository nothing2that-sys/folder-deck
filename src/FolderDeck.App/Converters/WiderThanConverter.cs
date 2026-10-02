using System;
using System.Globalization;
using System.Windows.Data;

namespace FolderDeck.App.Converters;

public sealed class WiderThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width))
        {
            return true;
        }

        var threshold = parameter is string text
                        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0d;

        return width > threshold;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
