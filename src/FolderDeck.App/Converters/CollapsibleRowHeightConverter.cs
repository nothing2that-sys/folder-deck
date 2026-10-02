using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FolderDeck.App.Converters;

public sealed class CollapsibleRowHeightConverter : IValueConverter
{
    private static readonly GridLengthConverter LengthConverter = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return GridLength.Auto;
        }

        if (parameter is string text)
        {
            try
            {

                if (LengthConverter.ConvertFrom(null, CultureInfo.InvariantCulture, text) is GridLength expanded)
                {
                    return expanded;
                }
            }
            catch (NotSupportedException)
            {

            }
        }

        return new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
