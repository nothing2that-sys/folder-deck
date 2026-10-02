using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class SortEntryPointTests
{
    [Fact]
    public async Task ChoosingACriterionChangesItAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(SortBy.Name, code.Entry!.SortBy);

        var saves = Count(code);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(SortBy.Size, code.Entry.SortBy);

        Assert.Equal(1, saves());
    }

    [Fact]
    public async Task ChoosingTheSameCriterionFlipsTheDirection()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        Assert.Equal(SortBy.Size, output.Entry!.SortBy);
        Assert.True(output.Entry.SortDesc);
        Assert.Equal(["b.dll", "a.dll"], output.Items.Select(i => i.Name));

        await output.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(SortBy.Size, output.Entry.SortBy);
        Assert.False(output.Entry.SortDesc);

        Assert.Equal(["a.dll", "b.dll"], output.Items.Select(i => i.Name));
        Assert.Equal(" · 크기 ↑", output.MetaSortText);
    }

    [Fact]
    public async Task ChoosingADifferentCriterionResetsToAscending()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        Assert.True(output.Entry!.SortDesc);

        await output.SetSortCommand.ExecuteAsync(SortBy.Name);

        Assert.Equal(SortBy.Name, output.Entry.SortBy);
        Assert.False(output.Entry.SortDesc);
        Assert.Equal(" · 이름 ↑", output.MetaSortText);
    }

    [Fact]
    public async Task SelectionSurvivesASortChange()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        var chosen = output.Items.Single(i => i.Name == "a.dll");
        output.SelectedItems = [chosen];
        Assert.Same(output, f.ViewModel.SelectionOwner);

        var cleared = 0;
        output.SelectionCleared += (_, _) => cleared++;

        await output.SetSortCommand.ExecuteAsync(SortBy.Name);

        Assert.Equal(0, cleared);

        Assert.Equal(
            [chosen.FullPath],
            output.SelectedItems.Select(i => i.FullPath));
        Assert.True(output.HasSelection);
        Assert.Same(output, f.ViewModel.SelectionOwner);
    }

    [Fact]
    public async Task NotificationsAccompanyTheChange()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Contains(nameof(FolderPanelViewModel.MetaSortText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.MetaText), seen);
        Assert.Equal(" · 수정 ↑", code.MetaSortText);
    }

    [Fact]
    public async Task ChoosingACriterionUpdatesTheSortIconToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal("▴", code.SortIcon);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(SortBy.Size, code.Entry!.SortBy);
        Assert.Equal("▴", code.SortIcon);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.True(code.Entry.SortDesc);
        Assert.Equal("▾", code.SortIcon);
    }

    [Fact]
    public async Task MetaPartsStillJoinIntoTheMetaLine()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);

        await code.SetSortCommand.ExecuteAsync(SortBy.Type);
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);

        await code.SetSortCommand.ExecuteAsync(SortBy.Type);
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
    }

    [Fact]
    public async Task TheSortSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.SetSortCommand.ExecuteAsync(SortBy.Type);
        await code.SetSortCommand.ExecuteAsync(SortBy.Type);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.Equal(SortBy.Type, entry.SortBy);
        Assert.True(entry.SortDesc);
    }

    [Fact]
    public async Task AnEmptyRotatingPanelHasNothingToSort()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        var saves = 0;
        rotating.SaveFolderEntry = () => saves++;

        await rotating.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Null(rotating.Entry);
        Assert.Null(rotating.MetaText);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task EachTileSortsOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Equal(SortBy.Modified, code.Entry!.SortBy);
        Assert.Equal(SortBy.Size, output.Entry!.SortBy);
        Assert.True(output.Entry.SortDesc);
    }

    [Fact]
    public async Task TheListViewTileSortsToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        Assert.True(panel.IsListView);
        Assert.Equal(SortBy.Name, panel.Entry!.SortBy);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Equal(SortBy.Modified, panel.Entry.SortBy);
        Assert.False(panel.Entry.SortDesc);
        Assert.True(panel.IsListView);
        Assert.Equal(" · 수정 ↑", panel.MetaSortText);
    }

    private static Func<int> Count(FolderPanelViewModel panel)
    {
        var saves = 0;
        var save = panel.SaveFolderEntry!;
        panel.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        return () => saves;
    }
}
