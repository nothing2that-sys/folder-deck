using System.Windows;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;

















public static class ColumnSort
{

    public static readonly DependencyProperty SortKeyProperty =
        DependencyProperty.RegisterAttached(
            "SortKey", typeof(SortBy?), typeof(ColumnSort), new PropertyMetadata(null));

    public static void SetSortKey(DependencyObject target, SortBy? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(SortKeyProperty, value);
    }

    public static SortBy? GetSortKey(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (SortBy?)target.GetValue(SortKeyProperty);
    }
}
