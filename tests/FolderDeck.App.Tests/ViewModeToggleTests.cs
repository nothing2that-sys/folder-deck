using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class ViewModeToggleTests
{
    [Fact]
    public async Task TogglingAdvancesTheValueAndSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(FolderViewMode.Details, code.Entry!.ViewMode);

        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);

        Assert.Equal(FolderViewMode.ExtraLargeIcons, code.Entry.ViewMode);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(
            FolderViewMode.ExtraLargeIcons,
            reloaded.Folders.Single(e => e.Path == f.CodePath).ViewMode);
    }

    [Fact]
    public async Task FiveIndicatorsMoveTogether()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.ToggleViewModeCommand.Execute(null);
        seen.Clear();
        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains(nameof(FolderPanelViewModel.IsDetailsView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.IsListView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.IsLargeIconView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.IsExtraLargeIconView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.MetaText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.ViewModeText), seen);

        Assert.False(code.IsDetailsView);
        Assert.False(code.IsListView);
        Assert.False(code.IsLargeIconView);
        Assert.True(code.IsExtraLargeIconView);
        Assert.Equal("extraLargeIcons", code.ViewModeText);
        Assert.Contains("extraLargeIcons", code.MetaText);
        Assert.DoesNotContain("details", code.MetaText);
    }

    [Fact]
    public async Task MetaPartsJoinIntoTheMetaLine()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);

        code.ToggleViewModeCommand.Execute(null);
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
    }

    [Fact]
    public async Task TogglingFourTimesComesBack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);

        Assert.Equal(FolderViewMode.Details, code.Entry!.ViewMode);
        Assert.True(code.IsDetailsView);
        Assert.False(code.IsListView);
    }

    [Fact]
    public async Task EachTileFlipsOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        code.ToggleViewModeCommand.Execute(null);

        Assert.True(code.IsLargeIconView);
        Assert.True(output.IsDetailsView);
        Assert.Equal(FolderViewMode.Details, output.Entry!.ViewMode);
    }

    [Fact]
    public async Task TogglingClearsTheSelection()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [code.Items[0]];
        Assert.Same(code, f.ViewModel.SelectionOwner);

        var cleared = 0;
        code.SelectionCleared += (_, _) => cleared++;

        code.ToggleViewModeCommand.Execute(null);

        Assert.Equal(1, cleared);
        Assert.Empty(code.SelectedItems);
        Assert.False(code.HasSelection);
        Assert.Null(f.ViewModel.SelectionOwner);
    }

    [Fact]
    public async Task TogglingWithNoSelectionMakesNoSelectionNoise()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.HasSelection);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.ToggleViewModeCommand.Execute(null);

        Assert.DoesNotContain(nameof(FolderPanelViewModel.SelectedItems), seen);
        Assert.DoesNotContain(nameof(FolderPanelViewModel.SelectionText), seen);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void AnEmptyRotatingPanelHasNothingToFlip()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        var saves = 0;
        rotating.SaveFolderEntry = () => saves++;

        rotating.ToggleViewModeCommand.Execute(null);

        Assert.False(rotating.IsDetailsView);
        Assert.False(rotating.IsListView);
        Assert.False(rotating.IsLargeIconView);
        Assert.False(rotating.IsExtraLargeIconView);
        Assert.Null(rotating.MetaText);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task SettingTheSameValueTwiceLeavesItUnchanged()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(FolderViewMode.Details, code.Entry!.ViewMode);

        code.SetViewModeCommand.Execute(FolderViewMode.Details);
        Assert.Equal(FolderViewMode.Details, code.Entry.ViewMode);

        code.SetViewModeCommand.Execute(FolderViewMode.Details);
        Assert.Equal(FolderViewMode.Details, code.Entry.ViewMode);
    }

    [Fact]
    public async Task TheMenuCommandCanSelectLargeIconsDirectly()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SetViewModeCommand.Execute(FolderViewMode.LargeIcons);

        Assert.Equal(FolderViewMode.LargeIcons, code.Entry!.ViewMode);
        Assert.True(code.IsLargeIconView);
        Assert.Equal("▩", code.ViewModeIcon);
        Assert.Contains("큰 아이콘", code.ViewModeTip);
    }

    [Fact]
    public async Task TheMenuCommandCanSelectExtraLargeIconsDirectly()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SetViewModeCommand.Execute(FolderViewMode.ExtraLargeIcons);

        Assert.Equal(FolderViewMode.ExtraLargeIcons, code.Entry!.ViewMode);
        Assert.True(code.IsExtraLargeIconView);
        Assert.False(code.IsLargeIconView);
        Assert.Equal("▣", code.ViewModeIcon);
        Assert.Contains("아주 큰 아이콘", code.ViewModeTip);
    }
}
