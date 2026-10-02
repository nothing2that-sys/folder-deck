using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class LayoutEditTests
{
    private static MainWindowFixture Editing()
    {
        var f = new MainWindowFixture();
        f.ViewModel.ToggleLayoutEdit();
        return f;
    }

    private static GridCellViewModel FreeCell(MainWindowFixture f) =>
        f.ViewModel.Cells.First(c => c.IsFree);

    [Fact]
    public void ViewModeIsTheDefaultAndTilesAreLocked()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        Assert.False(f.ViewModel.IsEditingLayout);
        Assert.True(f.ViewModel.IsViewMode);
        Assert.True(tile.IsViewMode);

        Assert.False(f.ViewModel.MoveTile(tile, 2, 2));
        Assert.False(f.ViewModel.ResizeTile(tile, 1, 1));
        Assert.Equal((0, 0, 2, 2), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ToggleFlipsEveryTileIntoEditMode()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ToggleLayoutEdit();

        Assert.True(f.ViewModel.IsEditingLayout);
        Assert.All(f.ViewModel.Tiles, t => Assert.True(t.IsEditing));
        Assert.All(f.ViewModel.Tiles, t => Assert.False(t.IsViewMode));

        f.ViewModel.ToggleLayoutEdit();

        Assert.False(f.ViewModel.IsEditingLayout);
        Assert.All(f.ViewModel.Tiles, t => Assert.False(t.IsEditing));
    }

    [Fact]
    public async Task RowClicksAreIgnoredWhileEditing()
    {
        using var f = Editing();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.True(f.Rotating.IsEmpty);
    }

    [Fact]
    public void MovingATileSnapsToTheCellAndPersists()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        Assert.True(f.ViewModel.MoveTile(tile, 2, 2));

        Assert.Equal((2, 2), (tile.CellX, tile.CellY));
        Assert.Equal((2, 2), (tile.Spec.CellX, tile.Spec.CellY));

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal((2, 2), (reloaded.Tiles![0].CellX, reloaded.Tiles[0].CellY));
    }

    [Fact]
    public void MovingOutsideTheGridIsRejected()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        Assert.False(f.ViewModel.MoveTile(tile, 3, 0));
        Assert.False(f.ViewModel.MoveTile(tile, 0, 3));
        Assert.Equal((0, 0), (tile.CellX, tile.CellY));
    }

    [Fact]
    public void MovingOntoAnotherTileIsRejectedNotPushedAside()
    {
        using var f = Editing();
        var first = f.ViewModel.Tiles[0];
        var second = f.ViewModel.Tiles[1];

        Assert.False(f.ViewModel.MoveTile(first, 2, 0));

        Assert.Equal((0, 0), (first.CellX, first.CellY));
        Assert.Equal((2, 0), (second.CellX, second.CellY));
    }

    [Fact]
    public void MovingIntoTheEmptyQuadrantWorks()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        Assert.True(f.ViewModel.MoveTile(tile, 2, 2));
        Assert.Equal(4, f.ViewModel.Cells.Count(c => c.IsFree));
        Assert.True(f.ViewModel.Cells.Single(c => c is { X: 0, Y: 0 }).IsFree);
    }

    [Fact]
    public void ResizingStaysInsideTheGrid()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[2];

        Assert.False(f.ViewModel.ResizeTile(tile, 2, 3));
        Assert.True(f.ViewModel.ResizeTile(tile, 4, 2));
        Assert.Equal((4, 2), (tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ResizingIntoAnotherTileIsRejected()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        Assert.False(f.ViewModel.ResizeTile(tile, 3, 2));
        Assert.Equal((2, 2), (tile.SpanX, tile.SpanY));

        Assert.True(f.ViewModel.ResizeTile(tile, 1, 1));
        Assert.Equal((1, 1), (tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ResizeAndMovePersistAcrossARestart()
    {
        using var f = Editing();
        f.ViewModel.ResizeTile(f.ViewModel.Tiles[0], 1, 1);
        f.ViewModel.MoveTile(f.ViewModel.Tiles[0], 3, 3);
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt, f.MacroEditor,
            f.FolderEditor, f.SelfLauncher);

        var tile = restarted.Tiles[0];
        Assert.Equal((3, 3, 1, 1), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void RemovingATileKeepsTheFolderRegistered()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];
        var name = f.Row("code").DisplayName;

        f.ViewModel.RemoveTile(tile);

        Assert.Equal(2, f.ViewModel.Tiles.Count);
        Assert.Equal(2, f.Workspace.Tiles!.Count);

        Assert.Equal(5, f.Workspace.Folders.Count);
        Assert.Contains(f.ViewModel.Rows, r => r.DisplayName == name);

        Assert.Equal(8, f.ViewModel.Cells.Count(c => c.IsFree));
    }

    [Fact]
    public void PinnedTileBecomesRotatingWithTheLowestFreeNumber()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles[0];

        f.ViewModel.ToggleTileKind(tile);

        Assert.True(tile.IsRotating);
        Assert.Equal(2, tile.RotationIndex);
        Assert.Equal("순환2", tile.KindTag);
        Assert.Null(tile.Spec.FolderId);
        Assert.True(tile.Panel.IsEmpty);
        Assert.True(tile.Panel.IsRotating);
    }

    [Fact]
    public async Task RotatingTileBecomesPinnedOnWhateverItIsShowing()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        f.ViewModel.ToggleLayoutEdit();
        var tile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        f.ViewModel.ToggleTileKind(tile);

        Assert.True(tile.IsPinned);
        Assert.Null(tile.RotationIndex);
        Assert.Equal(f.Row("문서").Entry.Id, tile.Spec.FolderId);
        Assert.Equal(f.DocsPath, tile.Panel.Entry!.Path);
    }

    [Fact]
    public void EmptyRotatingTileCannotBePinned()
    {
        using var f = Editing();
        var tile = f.ViewModel.Tiles.Single(t => t.IsRotating);

        f.ViewModel.ToggleTileKind(tile);

        Assert.True(tile.IsRotating);
        Assert.Contains("비어 있어", f.ViewModel.Message);
    }

    [Fact]
    public void SecondRotatingTileGetsNumberTwoAndRowsShowTheirTag()
    {
        using var f = Editing();

        Assert.All(f.ViewModel.Rows, r => Assert.False(r.ShowRotationTag));

        Assert.True(f.ViewModel.PlaceRotatingInCell(FreeCell(f)));

        var tags = f.ViewModel.Tiles.Where(t => t.IsRotating).Select(t => t.KindTag);
        Assert.Equal(["순환1", "순환2"], tags);

        Assert.True(f.Row("문서").ShowRotationTag);
        Assert.True(f.Row("작업").ShowRotationTag);
        Assert.Equal("순환1", f.Row("문서").RotationSlotText);
        Assert.Equal([1, 2], f.Row("문서").RotationChoices.Select(c => c.Slot));

        Assert.False(f.Row("code").ShowRotationTag);
        Assert.False(f.Row("산출물").ShowRotationTag);
    }

    [Fact]
    public void RemovingAPinnedTileGivesThatFolderItsRotationTagBack()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        Assert.False(f.Row("code").ShowRotationTag);

        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == f.Row("code").Entry.Id));

        Assert.True(f.Row("code").ShowRotationTag);
    }

    [Fact]
    public async Task AFolderAssignedToSlotTwoOpensInTheSlotTwoTile()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        var second = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);
        var first = f.ViewModel.Tiles.Single(t => t.RotationIndex == 1);

        f.Row("문서").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        f.ViewModel.ToggleLayoutEdit();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Equal(f.DocsPath, second.Panel.CurrentPath);
        Assert.True(first.Panel.IsEmpty);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.Equal(f.WorkPath, first.Panel.CurrentPath);
        Assert.Equal(f.DocsPath, second.Panel.CurrentPath);
    }

    [Fact]
    public void SlotOneIsStoredAsNullSoTheFileStaysQuiet()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));

        var row = f.Row("문서");
        row.RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        Assert.Equal(2, row.Entry.RotationSlot);

        row.RotationChoices.Single(c => c.Slot == 1).SelectCommand.Execute(null);
        Assert.Null(row.Entry.RotationSlot);
        Assert.Equal(1, row.RotationSlot);
    }

    [Fact]
    public async Task AMissingRotationSlotFallsBackToSlotOne()
    {
        using var f = new MainWindowFixture();
        f.Row("문서").Entry.RotationSlot = 2;
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
        Assert.Contains("순환2 칸 없음", f.ViewModel.Message);
    }

    [Fact]
    public void RemovingARotatingTileDemotesItsFoldersToSlotOne()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        var second = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);

        f.Row("문서").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        f.Row("설비 로그").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);
        Assert.Equal(2, f.Row("문서").RotationSlot);

        f.ViewModel.RemoveTile(second);

        Assert.Equal(1, f.Row("문서").RotationSlot);
        Assert.Null(f.Row("문서").Entry.RotationSlot);
        Assert.Equal(1, f.Row("설비 로그").RotationSlot);
        Assert.Contains("순환1로", f.ViewModel.Message);
    }

    [Fact]
    public void TurningARotatingTileIntoPinnedAlsoDemotesItsFolders()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        var second = f.ViewModel.Tiles.Single(t => t.RotationIndex == 2);
        f.Row("문서").RotationChoices.Single(c => c.Slot == 2).SelectCommand.Execute(null);

        second.Panel.Entry = f.Row("산출물").Entry;
        f.ViewModel.ToggleTileKind(second);

        Assert.True(second.IsPinned);
        Assert.Equal(1, f.Row("문서").RotationSlot);
    }

    [Fact]
    public async Task WithNoRotatingTileLeftTheClickSaysWhy()
    {
        using var f = Editing();
        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.IsRotating));
        f.ViewModel.ToggleLayoutEdit();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Null(f.ViewModel.RotatingPanel);
        Assert.Contains("순환 칸이 없다", f.ViewModel.Message);
    }

    [Fact]
    public async Task DroppingAFolderOnAFreeCellMakesAOneByOnePinnedTile()
    {
        using var f = Editing();
        var cell = FreeCell(f);

        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), cell));

        var tile = f.ViewModel.Tiles[^1];
        Assert.True(tile.IsPinned);
        Assert.Equal((cell.X, cell.Y, 1, 1), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
        Assert.Equal(f.DocsPath, tile.Panel.Entry!.Path);
        Assert.False(cell.IsFree);

        Assert.Equal(f.DocsPath, tile.Panel.CurrentPath);
        Assert.Equal("spec.md", Assert.Single(tile.Panel.Items).Name);
    }

    [Fact]
    public async Task ClickingAFreeCellAndPickingAFolderDoesTheSameThing()
    {
        using var f = Editing();
        var cell = FreeCell(f);

        f.ViewModel.BeginPlaceInCell(cell);
        Assert.True(f.ViewModel.IsFolderPickerOpen);
        Assert.Same(cell, f.ViewModel.PendingCell);

        await f.ViewModel.PlaceFolderAsync(f.Row("문서"));

        Assert.False(f.ViewModel.IsFolderPickerOpen);
        Assert.Null(f.ViewModel.PendingCell);
        Assert.Equal(f.DocsPath, f.ViewModel.Tiles[^1].Panel.CurrentPath);
    }

    [Fact]
    public async Task PuttingARemovedFolderBackShowsItsContentsRightAway()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.ToggleLayoutEdit();

        var code = f.ViewModel.Tiles[0];
        Assert.Equal(f.CodePath, code.Panel.CurrentPath);

        f.ViewModel.RemoveTile(code);
        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(f.Row("code"), FreeCell(f)));

        var again = f.ViewModel.Tiles[^1];
        Assert.Equal(f.CodePath, again.Panel.Entry!.Path);
        Assert.Equal(f.CodePath, again.Panel.CurrentPath);
        Assert.Equal(["Recipe", "Views", "Main.cs"], again.Panel.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task OccupiedCellsAndViewModeRejectPlacement()
    {
        using var f = new MainWindowFixture();
        var free = f.ViewModel.Cells.First(c => c.IsFree);

        Assert.False(await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), free));

        f.ViewModel.ToggleLayoutEdit();
        var taken = f.ViewModel.Cells.First(c => !c.IsFree);
        Assert.False(await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), taken));
        Assert.Equal(3, f.ViewModel.Tiles.Count);
    }

    [Fact]
    public async Task PlacedTilesSurviveARestart()
    {
        using var f = Editing();
        await f.ViewModel.PlaceFolderInCellAsync(f.Row("문서"), FreeCell(f));
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt, f.MacroEditor,
            f.FolderEditor, f.SelfLauncher);

        Assert.Equal(5, restarted.Tiles.Count);
        Assert.Equal(2, restarted.Tiles.Count(t => t.IsRotating));
        Assert.Equal([1, 2], restarted.Tiles.Where(t => t.IsRotating).Select(t => t.RotationIndex));
    }

    [Fact]
    public async Task LayoutPersistsButCurrentPathStillResetsToTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.OpenAsync(f.Rotating.Items.Single(i => i.Name == "Recipe"));

        f.ViewModel.ToggleLayoutEdit();
        f.ViewModel.MoveTile(f.ViewModel.Tiles[0], 2, 2);
        f.Store.Flush();

        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));
        Assert.DoesNotContain("Recipe", json);
        Assert.DoesNotContain("currentPath", json);

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt, f.MacroEditor,
            f.FolderEditor, f.SelfLauncher);
        await restarted.InitializeAsync();

        Assert.Equal((2, 2), (restarted.Tiles[0].CellX, restarted.Tiles[0].CellY));
        Assert.True(restarted.RotatingPanel!.IsEmpty);
        Assert.Equal(f.CodePath, restarted.Tiles[0].Panel.CurrentPath);
    }

    [Fact]
    public void ATileWhoseFolderVanishedIsDroppedAndReported()
    {
        using var f = new MainWindowFixture();
        f.Workspace.Tiles =
        [
            new TileSpec { Kind = TileKind.Pinned, FolderId = Guid.NewGuid(), CellX = 0, CellY = 0 },
            new TileSpec { Kind = TileKind.Rotating, CellX = 1, CellY = 0, RotationIndex = 1 },
        ];

        var vm = new MainViewModel(
            f.Workspace, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt,
            f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        Assert.Single(vm.Tiles);
        Assert.Contains("놓을 수 없는 타일 1개", vm.Message);
    }

    [Fact]
    public async Task InfoMessagesExpireButWarningsStay()
    {
        using var f = Editing();

        f.ViewModel.PlaceRotatingInCell(FreeCell(f));
        Assert.True(f.ViewModel.MessageIsTransient);
        Assert.False(f.ViewModel.MessageIsWarning);

        f.ViewModel.ExpireTransientMessage();
        Assert.False(f.ViewModel.HasMessage);

        foreach (var rotating in f.ViewModel.Tiles.Where(t => t.IsRotating).ToList())
        {
            f.ViewModel.RemoveTile(rotating);
        }

        f.ViewModel.ToggleLayoutEdit();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);

        f.ViewModel.ExpireTransientMessage();
        Assert.Contains("순환 칸이 없다", f.ViewModel.Message);
    }

    [Fact]
    public void ARefusalIsRedButDoesNotLinger()
    {
        using var f = Editing();

        f.ViewModel.ReportTileOverlap();

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.True(f.ViewModel.MessageIsTransient);
    }

    [Fact]
    public async Task AFolderThatAlreadyHasAPinnedTileCannotBePlacedAgain()
    {
        using var f = Editing();
        var row = f.Row("code");

        Assert.False(row.CanPlaceInGrid);
        Assert.True(row.HasPinnedTile);

        Assert.False(await f.ViewModel.PlaceFolderInCellAsync(row, FreeCell(f)));

        Assert.Equal(3, f.ViewModel.Tiles.Count);
        Assert.Contains("이미 상시 칸에 있다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
    }

    [Fact]
    public async Task RemovingThePinnedTileMakesTheFolderPlaceableAgain()
    {
        using var f = Editing();
        var row = f.Row("code");

        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == row.Entry.Id));
        Assert.True(row.CanPlaceInGrid);

        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(row, FreeCell(f)));
        Assert.False(row.CanPlaceInGrid);
    }

    [Fact]
    public void RotatingCellsAreUnaffectedByTheDuplicateRule()
    {
        using var f = Editing();

        Assert.True(f.ViewModel.PlaceRotatingInCell(FreeCell(f)));
        Assert.True(f.ViewModel.PlaceRotatingInCell(FreeCell(f)));

        Assert.Equal(3, f.ViewModel.Tiles.Count(t => t.IsRotating));
    }

    [Fact]
    public async Task APinnedFolderIsOfferedNoRotationChoices()
    {
        using var f = Editing();
        f.ViewModel.PlaceRotatingInCell(FreeCell(f));

        Assert.Empty(f.Row("code").RotationChoices);
        Assert.Empty(f.Row("산출물").RotationChoices);
        Assert.NotEmpty(f.Row("문서").RotationChoices);

        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == f.Row("code").Entry.Id));
        Assert.NotEmpty(f.Row("code").RotationChoices);

        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(f.Row("code"), FreeCell(f)));
        Assert.Empty(f.Row("code").RotationChoices);
    }
}
