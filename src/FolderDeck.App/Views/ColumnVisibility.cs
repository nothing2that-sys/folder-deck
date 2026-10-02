using System.Windows.Controls;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Views;











public static class ColumnVisibility
{











    public static bool ShouldShow(GridViewColumn column, bool showSize, bool showModified, bool showPosition) =>
        ColumnSort.GetSortKey(column) switch
        {
            SortBy.Size => showSize,
            SortBy.Modified => showModified,
            null => showPosition,
            _ => true,
        };
}
