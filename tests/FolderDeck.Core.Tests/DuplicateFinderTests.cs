using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Tests;

public sealed class DuplicateFinderTests
{
    private static readonly DateTime Stamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static FolderItem File(string name, long size) =>
        new(name, $@"C:\any\{name}", IsDirectory: false, size, Stamp);

    private static FolderItem Folder(string name) =>
        new(name, $@"C:\any\{name}", IsDirectory: true, Size: null, Stamp);

    [Fact]
    public void SameNameAndSizeAcrossTilesFormsAGroup()
    {
        var entries = new[]
        {
            ("Tile A", File("report.pdf", 100)),
            ("Tile B", File("report.pdf", 100)),
        };

        var groups = DuplicateFinder.FindGroups(entries);

        var group = Assert.Single(groups);
        Assert.Equal("report.pdf", group.Name);
        Assert.Equal(100, group.Size);
        Assert.Equal(2, group.Entries.Count);
    }

    [Fact]
    public void DifferentSizeIsNotAGroup()
    {
        var entries = new[]
        {
            ("Tile A", File("report.pdf", 100)),
            ("Tile B", File("report.pdf", 200)),
        };

        Assert.Empty(DuplicateFinder.FindGroups(entries));
    }

    [Fact]
    public void SingleOccurrenceIsNotAGroup()
    {
        var entries = new[] { ("Tile A", File("only.pdf", 100)) };

        Assert.Empty(DuplicateFinder.FindGroups(entries));
    }

    [Fact]
    public void FoldersAreNeverCandidates()
    {
        var entries = new[]
        {
            ("Tile A", Folder("same")),
            ("Tile B", Folder("same")),
        };

        Assert.Empty(DuplicateFinder.FindGroups(entries));
    }

    [Fact]
    public void NameMatchIsCaseInsensitiveButDisplaysFirstSeenCasing()
    {
        var entries = new[]
        {
            ("Tile A", File("Report.PDF", 100)),
            ("Tile B", File("report.pdf", 100)),
        };

        var group = Assert.Single(DuplicateFinder.FindGroups(entries));
        Assert.Equal("Report.PDF", group.Name);
    }

    [Fact]
    public void GroupsAreOrderedBySizeDescending()
    {
        var entries = new[]
        {
            ("Tile A", File("small.bin", 10)),
            ("Tile B", File("small.bin", 10)),
            ("Tile A", File("big.bin", 1_000)),
            ("Tile B", File("big.bin", 1_000)),
        };

        var groups = DuplicateFinder.FindGroups(entries);

        Assert.Equal(["big.bin", "small.bin"], groups.Select(g => g.Name));
    }
}
