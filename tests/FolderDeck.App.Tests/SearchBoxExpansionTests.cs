namespace FolderDeck.App.Tests;

public sealed class SearchBoxExpansionTests
{
    [Fact]
    public void StartsCollapsed()
    {
        using var f = new MainWindowFixture();

        Assert.All(f.ViewModel.Panels, p => Assert.False(p.IsSearchExpanded));
    }

    [Fact]
    public void ExpandsOnDemand()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        panel.ExpandSearch();

        Assert.True(panel.IsSearchExpanded);
    }

    [Fact]
    public void CollapsesWhenFocusLeavesAndTheQueryIsEmpty()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;
        panel.ExpandSearch();

        panel.CollapseSearchIfEmpty();

        Assert.False(panel.IsSearchExpanded);
    }

    [Fact]
    public async Task StaysExpandedWhileAQueryIsPresent()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ExpandSearch();
        panel.SearchText = "recipe";

        panel.CollapseSearchIfEmpty();

        Assert.True(panel.IsSearchExpanded);
        Assert.Single(panel.DisplayItems);
    }

    [Fact]
    public async Task CollapsesOnceTheQueryIsCleared()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ExpandSearch();
        panel.SearchText = "recipe";
        panel.CollapseSearchIfEmpty();
        Assert.True(panel.IsSearchExpanded);

        panel.SearchText = string.Empty;
        panel.CollapseSearchIfEmpty();

        Assert.False(panel.IsSearchExpanded);
    }

    [Fact]
    public void EachPanelExpandsIndependently()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.Panels[0].ExpandSearch();

        Assert.True(f.ViewModel.Panels[0].IsSearchExpanded);
        Assert.False(f.Rotating.IsSearchExpanded);
    }

    [Fact]
    public async Task IsNotPersisted()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        panel.ExpandSearch();
        panel.SearchText = "recipe";

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        f.Store.Flush();
        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("searchExpanded", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("searchText", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipe", json, StringComparison.OrdinalIgnoreCase);

        using var restarted = new MainWindowFixture();
        Assert.All(restarted.ViewModel.Panels, p => Assert.False(p.IsSearchExpanded));
    }

    [Fact]
    public async Task NavigatingClearsTheQuerySoItCanCollapseAgain()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ExpandSearch();
        panel.SearchText = "recipe";

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        Assert.Equal(string.Empty, panel.SearchText);
        panel.CollapseSearchIfEmpty();
        Assert.False(panel.IsSearchExpanded);
    }

    [Fact]
    public async Task CloseSearchClosesEvenWithAQueryAndRestoresTheList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var all = panel.Items.Count;

        panel.ExpandSearch();
        panel.SearchText = "recipe";
        Assert.Single(panel.DisplayItems);

        panel.CloseSearch();

        Assert.False(panel.IsSearchExpanded);
        Assert.Equal(string.Empty, panel.SearchText);
        Assert.Equal(all, panel.DisplayItems.Count);
        Assert.False(panel.ShowNoResults);
    }

    [Fact]
    public void CloseSearchOnAnAlreadyClosedPanelIsHarmless()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        panel.CloseSearch();

        Assert.False(panel.IsSearchExpanded);
        Assert.Equal(string.Empty, panel.SearchText);
    }

    [Fact]
    public void ToggleCommandFlipsTheExpandedState()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;
        Assert.False(panel.IsSearchExpanded);

        panel.ToggleSearchExpandedCommand.Execute(null);
        Assert.True(panel.IsSearchExpanded);

        panel.ToggleSearchExpandedCommand.Execute(null);
        Assert.False(panel.IsSearchExpanded);
    }

    [Fact]
    public async Task ClosingCancelsARunningRecursiveSearch()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ExpandSearch();
        panel.IncludeSubfolders = true;
        panel.SearchText = "n";

        panel.CloseSearch();

        Assert.False(panel.IsSearchExpanded);
        Assert.False(panel.IsSearching);

        await Task.Delay(600);
        Assert.False(panel.IsSearching);
        Assert.Equal(panel.Items.Count, panel.DisplayItems.Count);
    }
}
