using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Comparison;

public sealed record DuplicateGroupEntry(string TileLabel, FolderItem Item);

public sealed record DuplicateGroup(string Name, long Size, IReadOnlyList<DuplicateGroupEntry> Entries);

public static class DuplicateFinder
{
    public static IReadOnlyList<DuplicateGroup> FindGroups(
        IEnumerable<(string TileLabel, FolderItem Item)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(entry => !entry.Item.IsDirectory && entry.Item.Size is not null)
            .GroupBy(
                entry => (Key: entry.Item.Name.ToUpperInvariant(), Size: entry.Item.Size!.Value))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateGroup(
                group.First().Item.Name,
                group.Key.Size,
                group.Select(entry => new DuplicateGroupEntry(entry.TileLabel, entry.Item)).ToList()))
            .OrderByDescending(group => group.Size)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
