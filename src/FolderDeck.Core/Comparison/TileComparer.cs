using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Comparison;

public enum CompareStatus
{

    Unset,

    OnlyHere,

    Different,

    Same,
}

public sealed record CompareEntry(FolderItem Item, CompareStatus Status);

public static class TileComparer
{
    public static (IReadOnlyList<CompareEntry> Left, IReadOnlyList<CompareEntry> Right) Compare(
        IReadOnlyList<FolderItem> left, IReadOnlyList<FolderItem> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftByName = left.ToLookup(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var rightByName = right.ToLookup(item => item.Name, StringComparer.OrdinalIgnoreCase);

        var leftResult = left
            .Select(item => new CompareEntry(item, StatusOf(item, rightByName[item.Name].FirstOrDefault())))
            .ToList();

        var rightResult = right
            .Select(item => new CompareEntry(item, StatusOf(item, leftByName[item.Name].FirstOrDefault())))
            .ToList();

        return (leftResult, rightResult);
    }

    private static CompareStatus StatusOf(FolderItem item, FolderItem? counterpart)
    {
        if (counterpart is null)
        {
            return CompareStatus.OnlyHere;
        }

        if (item.IsDirectory != counterpart.IsDirectory)
        {
            return CompareStatus.Different;
        }

        if (item.IsDirectory)
        {
            return CompareStatus.Same;
        }

        return item.Size == counterpart.Size && item.ModifiedUtc == counterpart.ModifiedUtc
            ? CompareStatus.Same
            : CompareStatus.Different;
    }
}
