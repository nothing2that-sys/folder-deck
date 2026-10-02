using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;










public sealed class ColumnVisibilityTests
{

    private static FolderPanelViewModel BareSearchPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: null);


    private static FolderPanelViewModel BareFolderPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: false,
        reportError: null,
        reportRejection: null);

    [Fact]
    public async Task TurningOffShowSizeFlipsTheFolderEntryAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.Entry!.ShowSize);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        code.ToggleShowSizeCommand.Execute(null);

        Assert.False(code.Entry!.ShowSize);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task TurningOffShowModifiedFlipsTheFolderEntryAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.Entry!.ShowModified);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        code.ToggleShowModifiedCommand.Execute(null);

        Assert.False(code.Entry!.ShowModified);
        Assert.Equal(1, saves);
    }


    [Fact]
    public async Task TogglingBackRestoresShowSize()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        code.ToggleShowSizeCommand.Execute(null);
        code.ToggleShowSizeCommand.Execute(null);

        Assert.True(code.Entry!.ShowSize);
    }






    [Fact]
    public async Task TogglingShowSizeDoesNotReenumerateTheListing()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var itemsBeforeToggle = code.Items;

        code.ToggleShowSizeCommand.Execute(null);

        Assert.Same(itemsBeforeToggle, code.Items);
    }


    [Fact]
    public async Task TheChoiceTravelsWithTheFolderInARotatingSlot()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        Assert.True(panel.Entry!.ShowSize);

        panel.ToggleShowSizeCommand.Execute(null);
        Assert.False(panel.Entry!.ShowSize);


        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.True(panel.Entry!.ShowSize);


        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.False(panel.Entry!.ShowSize);
    }


    [Fact]
    public async Task EachTileTogglesOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.Panels[0].ToggleShowSizeCommand.Execute(null);

        Assert.False(f.ViewModel.Panels[0].Entry!.ShowSize);
        Assert.True(f.ViewModel.Panels[1].Entry!.ShowSize);
    }


    [Fact]
    public async Task TheChoiceSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.ToggleShowSizeCommand.Execute(null);
        code.ToggleShowModifiedCommand.Execute(null);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.False(entry.ShowSize);
        Assert.False(entry.ShowModified);


        Assert.Equal(SortBy.Name, entry.SortBy);
        Assert.True(entry.FoldersFirst);
    }


    [Fact]
    public void AnEmptyPanelHasNothingToToggle()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        rotating.ToggleShowSizeCommand.Execute(null);
        rotating.ToggleShowModifiedCommand.Execute(null);

        Assert.True(rotating.IsEmpty);
    }


    [Fact]
    public void ThereIsNoCommandToHideTheNameColumn()
    {
        var memberNames = typeof(FolderPanelViewModel)
            .GetMembers()
            .Select(m => m.Name);

        Assert.DoesNotContain("ToggleShowNameCommand", memberNames);
    }





    [Fact]
    public void TogglingShowSizeOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowSizeCommand.Execute(null);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void TogglingShowModifiedOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowModifiedCommand.Execute(null);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);
    }





    [Fact]
    public void TogglingShowPositionOnAFolderTileIsANoOp()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.Equal(0, saves);
    }





    [Fact]
    public void TogglingShowSizeOnASearchTileFlipsCurrentShowSize()
    {
        var panel = BareSearchPanel();
        Assert.True(panel.CurrentShowSize);

        panel.ToggleShowSizeCommand.Execute(null);
        Assert.False(panel.CurrentShowSize);

        panel.ToggleShowSizeCommand.Execute(null);
        Assert.True(panel.CurrentShowSize);
    }






    [Fact]
    public void CurrentShowSizeAndCurrentShowModifiedFollowTheFolderEntry()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };

        Assert.True(panel.CurrentShowSize);
        Assert.True(panel.CurrentShowModified);

        panel.Entry.ShowSize = false;
        panel.Entry.ShowModified = false;

        Assert.False(panel.CurrentShowSize);
        Assert.False(panel.CurrentShowModified);
    }





    [Fact]
    public void TogglingShowPositionOnASearchTileFlipsCurrentShowPosition()
    {
        var panel = BareSearchPanel();
        Assert.True(panel.CurrentShowPosition);

        panel.ToggleShowPositionCommand.Execute(null);
        Assert.False(panel.CurrentShowPosition);
    }

    private static async Task WaitForSearchToSettle(FolderPanelViewModel panel)
    {
        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }
    }







    [Fact]
    public async Task TogglingShowPositionDuringARecursiveSearchFlipsCurrentShowPosition()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);
        Assert.True(panel.NeedsLocationColumn);
        Assert.True(panel.CurrentShowPosition);

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.False(panel.CurrentShowPosition);
    }





    [Fact]
    public async Task TogglingShowPositionDuringARecursiveSearchNotifiesCurrentShowPosition()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.Contains(nameof(FolderPanelViewModel.CurrentShowPosition), seen);
    }





    [Fact]
    public async Task TogglingShowPositionOnAnOrdinaryFolderTileStillDoesNothing()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        Assert.False(panel.NeedsLocationColumn);
        var before = panel.CurrentShowPosition;

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.Equal(before, panel.CurrentShowPosition);
        Assert.DoesNotContain(nameof(FolderPanelViewModel.CurrentShowPosition), seen);
    }
}
