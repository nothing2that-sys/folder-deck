using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Converters;

public sealed class TileGapToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int gap ? gap / 2d : AppSettings.DefaultTileGap / 2d);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
