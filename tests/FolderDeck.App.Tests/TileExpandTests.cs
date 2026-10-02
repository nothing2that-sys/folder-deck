using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class TileExpandTests
{
    [Fact]
    public void EffectivePlacementMatchesStoredValuesWhenNotExpanded()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        Assert.False(tile.IsExpanded);
        Assert.Equal(tile.CellX, tile.EffectiveCellX);
        Assert.Equal(tile.CellY, tile.EffectiveCellY);
        Assert.Equal(tile.SpanX, tile.EffectiveSpanX);
        Assert.Equal(tile.SpanY, tile.EffectiveSpanY);
    }

    [Fact]
    public void EffectivePlacementFillsTheGridWhenExpanded()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        f.ViewModel.ExpandedTile = tile;

        Assert.Equal(0, tile.EffectiveCellX);
        Assert.Equal(0, tile.EffectiveCellY);
        Assert.Equal(f.ViewModel.GridCols, tile.EffectiveSpanX);
        Assert.Equal(f.ViewModel.GridRows, tile.EffectiveSpanY);
    }

    [Fact]
    public void TogglingExpansionOnThenOffLeavesStoredPlacementUnchanged()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];
        var (cellX, cellY, spanX, spanY) = (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY);

        f.ViewModel.ExpandedTile = tile;
        f.ViewModel.ExpandedTile = null;

        Assert.Equal(cellX, tile.CellX);
        Assert.Equal(cellY, tile.CellY);
        Assert.Equal(spanX, tile.SpanX);
        Assert.Equal(spanY, tile.SpanY);
    }

    [Fact]
    public void ExpandingATileHidesEveryOtherTile()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        f.ViewModel.ExpandedTile = tile;

        Assert.True(tile.IsExpanded);
        Assert.False(tile.IsHiddenByExpansion);
        Assert.All(f.ViewModel.Tiles.Where(t => !ReferenceEquals(t, tile)), other =>
        {
            Assert.False(other.IsExpanded);
            Assert.True(other.IsHiddenByExpansion);
        });
    }

    [Fact]
    public void ClearingExpandedTileLowersBothFlagsOnEveryTile()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];
        f.ViewModel.ExpandedTile = tile;

        Assert.True(tile.IsExpanded);
        Assert.Contains(f.ViewModel.Tiles, t => t.IsHiddenByExpansion);

        f.ViewModel.ExpandedTile = null;

        Assert.All(f.ViewModel.Tiles, t =>
        {
            Assert.False(t.IsExpanded);
            Assert.False(t.IsHiddenByExpansion);
        });
    }

    [Fact]
    public void LayoutEditCannotBeToggledWhileATileIsExpanded()
    {
        using var f = new MainWindowFixture();
        Assert.True(f.ViewModel.ToggleLayoutEditCommand.CanExecute(null));

        f.ViewModel.ExpandedTile = f.ViewModel.Tiles[0];

        Assert.False(f.ViewModel.ToggleLayoutEditCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShowingAFolderInAnotherTileCollapsesTheExpandedTile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.ExpandedTile = f.ViewModel.Tiles[0];

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Null(f.ViewModel.ExpandedTile);
    }

    [Fact]
    public async Task ShowingAFolderInTheExpandedTileItselfKeepsExpansion()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var rotatingTile = f.ViewModel.Tiles.Single(t => ReferenceEquals(t.Panel, f.Rotating));
        f.ViewModel.ExpandedTile = rotatingTile;

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Same(rotatingTile, f.ViewModel.ExpandedTile);
    }

    [Fact]
    public void SerializingTheWorkspaceIsByteIdenticalWhetherOrNotATileIsExpanded()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];
        var file = f.Paths.WorkspaceFile(f.Workspace.Id);

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        var before = File.ReadAllText(file);

        f.ViewModel.ExpandedTile = tile;

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        var after = File.ReadAllText(file);

        Assert.Equal(before, after);
        Assert.Equal(11, Workspace.CurrentSchemaVersion);
    }

    [Fact]
    public void RemovingTheExpandedTilesFolderClearsExpandedTile()
    {
        using var f = new MainWindowFixture();
        var codeRow = f.Row("code");
        var tile = f.ViewModel.Tiles.Single(t => t.IsPinned && t.Spec.FolderId == codeRow.Entry.Id);
        f.ViewModel.ExpandedTile = tile;

        f.ViewModel.RemoveFolder(codeRow);

        Assert.Null(f.ViewModel.ExpandedTile);
    }

    [Fact]
    public void RemovingTheExpandedTilesFolderLowersTheHiddenFlagOnSurvivors()
    {
        using var f = new MainWindowFixture();
        var codeRow = f.Row("code");
        var tile = f.ViewModel.Tiles.Single(t => t.IsPinned && t.Spec.FolderId == codeRow.Entry.Id);
        f.ViewModel.ExpandedTile = tile;

        var survivors = f.ViewModel.Tiles.Where(t => !ReferenceEquals(t, tile)).ToList();
        Assert.NotEmpty(survivors);
        Assert.All(survivors, t => Assert.True(t.IsHiddenByExpansion));

        f.ViewModel.RemoveFolder(codeRow);

        Assert.All(f.ViewModel.Tiles, t => Assert.False(t.IsHiddenByExpansion));
    }

    [Fact]
    public void RemovingADifferentFoldersRegistrationLeavesExpansionAlone()
    {
        using var f = new MainWindowFixture();
        var codeRow = f.Row("code");
        var outputRow = f.Row("산출물");
        var tile = f.ViewModel.Tiles.Single(t => t.IsPinned && t.Spec.FolderId == codeRow.Entry.Id);
        f.ViewModel.ExpandedTile = tile;

        f.ViewModel.RemoveFolder(outputRow);

        Assert.Same(tile, f.ViewModel.ExpandedTile);
        Assert.True(tile.IsExpanded);
    }
}
