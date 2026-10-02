using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class LeftLayoutTests
{
    [Fact]
    public void NothingIsCollapsedByDefault()
    {
        using var f = new MainWindowFixture();

        Assert.False(f.ViewModel.FoldersCollapsed);
        Assert.False(f.ViewModel.LowerCollapsed);
        Assert.True(f.ViewModel.ShowLeftSplitter);

        Assert.Null(f.Workspace.LeftLayout);
    }

    [Fact]
    public void EachAreaTogglesIndependently()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ToggleFolders();
        Assert.True(f.ViewModel.FoldersCollapsed);
        Assert.False(f.ViewModel.LowerCollapsed);

        f.ViewModel.ToggleLower();
        Assert.True(f.ViewModel.LowerCollapsed);

        f.ViewModel.ToggleFolders();
        Assert.False(f.ViewModel.FoldersCollapsed);
        Assert.True(f.ViewModel.LowerCollapsed);
    }

    [Fact]
    public void SplitterHidesWhenEitherSideIsCollapsed()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.FoldersCollapsed = true;
        Assert.False(f.ViewModel.ShowLeftSplitter);

        f.ViewModel.FoldersCollapsed = false;
        Assert.True(f.ViewModel.ShowLeftSplitter);

        f.ViewModel.LowerCollapsed = true;
        Assert.False(f.ViewModel.ShowLeftSplitter);
    }

    [Fact]
    public void CollapsedStateSurvivesAReopen()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.FoldersCollapsed = true;
        f.ViewModel.LowerCollapsed = true;
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var reopened = new MainViewModel(
            reloaded, f.Store, new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
            f.Engine, f.Prompt, f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        Assert.True(reopened.FoldersCollapsed);
        Assert.True(reopened.LowerCollapsed);
    }

    [Fact]
    public void OnlyTheCollapsedSideIsRemembered()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.LowerCollapsed = true;
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.False(reloaded.LeftLayout!.FoldersCollapsed);
        Assert.True(reloaded.LeftLayout.LowerCollapsed);
    }

    [Fact]
    public void CollapsingCreatesTheSlotOnAWorkspaceThatLacksIt()
    {
        using var f = new MainWindowFixture();
        Assert.Null(f.Workspace.LeftLayout);

        f.ViewModel.FoldersCollapsed = true;

        Assert.NotNull(f.Workspace.LeftLayout);
        f.Store.Flush();
        Assert.Equal(
            Workspace.CurrentSchemaVersion,
            f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow().SchemaVersion);
    }

    [Fact]
    public async Task CollapsingDoesNotBreakTheRotatingPanelControlFlow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.FoldersCollapsed = true;

        Assert.Equal(5, f.ViewModel.Rows.Count);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
    }

    [Fact]
    public void TogglingTheSameValueTwiceDoesNotChurn()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.FoldersCollapsed = true;
        f.ViewModel.FoldersCollapsed = true;

        Assert.True(f.ViewModel.FoldersCollapsed);
    }

    [Fact]
    public void TheWholePanelStartsExpanded()
    {
        using var f = new MainWindowFixture();

        Assert.False(f.ViewModel.LeftPanelCollapsed);

        Assert.Null(f.Workspace.LeftLayout);
    }

    [Fact]
    public void CollapsingTheWholePanelSurvivesAReopen()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ToggleLeftPanel();
        Assert.True(f.ViewModel.LeftPanelCollapsed);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        Assert.True(reloaded.LeftLayout!.LeftPanelCollapsed);

        var reopened = new MainViewModel(
            reloaded, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine,
            f.Prompt, f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        Assert.True(reopened.LeftPanelCollapsed);
        Assert.Equal("▶", reopened.LeftPanelToggleText);
    }

    [Fact]
    public void TheWholePanelToggleDoesNotDisturbTheInnerTwo()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ToggleFolders();

        f.ViewModel.ToggleLeftPanel();
        f.ViewModel.ToggleLeftPanel();

        Assert.False(f.ViewModel.LeftPanelCollapsed);
        Assert.True(f.ViewModel.FoldersCollapsed);
        Assert.False(f.ViewModel.LowerCollapsed);
    }

    [Fact]
    public void TheToggleTextFollowsTheStoredValue()
    {
        using var f = new MainWindowFixture();
        var seen = new List<string?>();
        f.ViewModel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        f.ViewModel.ToggleLeftPanel();

        Assert.Contains(nameof(MainViewModel.LeftPanelCollapsed), seen);
        Assert.Contains(nameof(MainViewModel.LeftPanelToggleText), seen);
        Assert.Contains(nameof(MainViewModel.LeftPanelToggleTip), seen);
        Assert.Equal("▶", f.ViewModel.LeftPanelToggleText);
    }

    [Fact]
    public void SettingTheSameValueChangesNothing()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.LeftPanelCollapsed = false;

        Assert.Null(f.Workspace.LeftLayout);
    }
}
