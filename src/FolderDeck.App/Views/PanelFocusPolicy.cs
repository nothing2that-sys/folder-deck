using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FolderDeck.App.Views;

internal static class PanelFocusPolicy
{
    public static bool ShouldFocusPanelOnLeftClick(DependencyObject? originalSource)
    {
        for (var current = originalSource; current is not null; current = ParentOf(current))
        {
            if (current is ButtonBase)
            {
                return false;
            }
        }

        return true;
    }

    private static DependencyObject? ParentOf(DependencyObject current) =>
        current is Visual visual
            ? VisualTreeHelper.GetParent(visual) ?? LogicalTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);
}
