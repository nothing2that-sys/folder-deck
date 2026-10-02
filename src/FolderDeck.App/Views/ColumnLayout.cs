using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FolderDeck.App.Views;

public static class ColumnLayout
{

    public const double RightMargin = 32;

    public const double MinFlexWidth = 160;

    public const double RowChrome = 6;

    public static readonly DependencyProperty IsFlexibleProperty =
        DependencyProperty.RegisterAttached(
            "IsFlexible", typeof(bool), typeof(ColumnLayout), new PropertyMetadata(false));

    public static void SetIsFlexible(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsFlexibleProperty, value);
    }

    public static bool GetIsFlexible(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (bool)target.GetValue(IsFlexibleProperty);
    }

    public static GridViewColumn? FlexColumn(GridView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Columns.FirstOrDefault(GetIsFlexible);
    }

    public static double FlexWidth(
        double viewportWidth, double otherColumnsWidth, double rightMargin, double minWidth)
    {
        if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
        {
            return minWidth;
        }

        var available = Math.Floor(viewportWidth - otherColumnsWidth - rightMargin - RowChrome);
        return double.IsFinite(available) ? Math.Max(minWidth, available) : minWidth;
    }
}
