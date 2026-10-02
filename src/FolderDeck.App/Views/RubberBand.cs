using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FolderDeck.App.Views;


public enum ListHitKind
{

    Empty,


    Item,


    None,
}












public static class RubberBand
{


























    public static ListHitKind Classify(IReadOnlyList<Type> ancestors)
    {
        ArgumentNullException.ThrowIfNull(ancestors);

        if (ancestors.Any(t => Is<ScrollBar>(t) || Is<Thumb>(t)))
        {
            return ListHitKind.None;
        }

        return ancestors.Any(Is<ListViewItem>) ? ListHitKind.Item : ListHitKind.Empty;

        static bool Is<T>(Type candidate) => typeof(T).IsAssignableFrom(candidate);
    }














    public static IReadOnlyList<Type> AncestorTypes(object? source, DependencyObject? stopAt)
    {
        var chain = new List<Type>();

        for (var node = source as DependencyObject; node is not null && node != stopAt;)
        {
            chain.Add(node.GetType());
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return chain;
    }


    public static Rect Between(Point a, Point b) => new(a, b);








    public static bool PastThreshold(Point origin, Point now) =>
        Math.Abs(now.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(now.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;









    public static IReadOnlyList<object> Kept(bool adds, IReadOnlyList<object> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        return adds ? [.. selected] : [];
    }










    public static HashSet<object> Wanted(
        IReadOnlyList<object> kept, IEnumerable<(object Item, Rect Bounds)> rows, Rect band)
    {
        ArgumentNullException.ThrowIfNull(kept);
        ArgumentNullException.ThrowIfNull(rows);

        var wanted = new HashSet<object>(kept);

        foreach (var (item, bounds) in rows)
        {
            if (Touches(band, bounds))
            {
                wanted.Add(item);
            }
        }

        return wanted;
    }









    public static bool Touches(Rect band, Rect item) =>
        band.Left < item.Right && band.Right > item.Left
        && band.Top < item.Bottom && band.Bottom > item.Top;
}













public static class ItemDragThreshold
{

    public static bool PastThreshold(Point origin, Point now) =>
        !(Math.Abs(now.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
          && Math.Abs(now.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance);
}
