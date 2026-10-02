namespace FolderDeck.App.Tests;

public sealed class FindDuplicateGroupsTests
{
    [Fact]
    public async Task FindsADuplicateAcrossTwoDifferentOpenTiles()
    {
        using var f = new MainWindowFixture();
        File.WriteAllText(Path.Combine(f.CodePath, "shared.bin"), "same size");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared.bin"), "same size");
        await f.ViewModel.InitializeAsync();

        var groups = f.ViewModel.FindDuplicateGroups();

        var group = Assert.Single(groups, g => g.Name == "shared.bin");
        Assert.Equal(2, group.Entries.Count);
        Assert.Contains(group.Entries, e => e.Item.FullPath == Path.Combine(f.CodePath, "shared.bin"));
        Assert.Contains(group.Entries, e => e.Item.FullPath == Path.Combine(f.OutputPath, "shared.bin"));
    }

    [Fact]
    public async Task EachEntryCarriesItsOwnTilesLabelNotTheOthers()
    {
        using var f = new MainWindowFixture();
        File.WriteAllText(Path.Combine(f.CodePath, "shared.bin"), "x");
        File.WriteAllText(Path.Combine(f.OutputPath, "shared.bin"), "x");
        await f.ViewModel.InitializeAsync();

        var group = Assert.Single(f.ViewModel.FindDuplicateGroups());

        var codeEntry = group.Entries.Single(e => e.Item.FullPath.StartsWith(f.CodePath, StringComparison.Ordinal));
        var outputEntry = group.Entries.Single(e => e.Item.FullPath.StartsWith(f.OutputPath, StringComparison.Ordinal));
        Assert.NotEqual(codeEntry.TileLabel, outputEntry.TileLabel);
    }

    [Fact]
    public async Task NoOverlapReturnsNoGroups()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.Empty(f.ViewModel.FindDuplicateGroups());
    }
}
