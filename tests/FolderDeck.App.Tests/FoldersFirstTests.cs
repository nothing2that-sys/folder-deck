using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class FoldersFirstTests
{
    [Fact]
    public async Task FoldersComeFirstByDefault()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.True(code.FoldersFirst);
        Assert.Equal(["Recipe", "Views", "Main.cs"], code.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task TurningItOffMergesFoldersIntoTheFiles()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.False(code.FoldersFirst);

        Assert.Equal(["Main.cs", "Recipe", "Views"], code.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task TogglingBackRestoresTheGrouping()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.True(code.FoldersFirst);
        Assert.Equal(["Recipe", "Views", "Main.cs"], code.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task TheMetaLineSaysWhenItIsOff()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(" · 이름 ↑", code.MetaSortText);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        Assert.Equal(" · 이름 ↑ · 통합", code.MetaSortText);

        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
    }

    [Fact]
    public async Task ChangingTheCriterionKeepsTheGroupingChoice()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.False(code.FoldersFirst);
        Assert.Equal(SortBy.Modified, code.Entry!.SortBy);
    }

    [Fact]
    public async Task TogglingEitherOneLeavesTheOtherAlone()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(SortBy.Name, code.Entry!.SortBy);
        Assert.Equal("▴", code.SortIcon);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal(SortBy.Name, code.Entry.SortBy);
        Assert.Equal("∪▴", code.SortIcon);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.False(code.FoldersFirst);
        Assert.Equal(SortBy.Size, code.Entry.SortBy);
        Assert.Equal("∪▴", code.SortIcon);
    }

    [Fact]
    public async Task EachTileGroupsOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.Panels[0].ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.False(f.ViewModel.Panels[0].FoldersFirst);
        Assert.True(f.ViewModel.Panels[1].FoldersFirst);
    }

    [Fact]
    public async Task TheChoiceSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal(1, saves);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.False(entry.FoldersFirst);

        Assert.Equal(SortBy.Name, entry.SortBy);
        Assert.False(entry.SortDesc);
    }

    [Fact]
    public async Task AnEmptyPanelHasNothingToRegroup()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        await rotating.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.True(rotating.FoldersFirst);
    }
}
