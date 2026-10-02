namespace FolderDeck.App.Tests;

public sealed class SearchTests
{

    private static async Task WaitForSearch(FolderPanelViewModel panel, int expectedAtLeast)
    {
        for (var i = 0; i < 200; i++)
        {
            if (!panel.IsSearching && panel.DisplayItems.Count >= expectedAtLeast)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"검색이 끝나지 않았다: IsSearching={panel.IsSearching}, {panel.DisplayItems.Count}건");
    }

    private static async Task<FolderPanelViewModel> CodePanel(MainWindowFixture f)
    {
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        return f.Rotating;
    }

    [Fact]
    public async Task FilterNarrowsTheCurrentListImmediately()
    {
        using var f = new MainWindowFixture();
        var panel = await CodePanel(f);
        var all = panel.DisplayItems.Count;

        panel.SearchText = "recipe";

        Assert.False(panel.IsSearching);
        Assert.Equal("Recipe", Assert.Single(panel.DisplayItems).Name);
        Assert.True(panel.DisplayItems.Count < all);

        Assert.Equal(all, panel.Items.Count);
    }

    [Fact]
    public async Task ClearingTheQueryRestoresTheList()
    {
        using var f = new MainWindowFixture();
        var panel = await CodePanel(f);
        var all = panel.Items.Count;

        panel.SearchText = "recipe";
        panel.SearchText = string.Empty;

        Assert.Equal(all, panel.DisplayItems.Count);
        Assert.False(panel.HasSearchText);
        Assert.False(panel.ShowNoResults);
    }

    [Fact]
    public async Task FilterIsCaseInsensitivePartialMatch()
    {
        using var f = new MainWindowFixture();
        var panel = await CodePanel(f);

        panel.SearchText = "MAIN";
        Assert.Equal("Main.cs", Assert.Single(panel.DisplayItems).Name);

        panel.SearchText = "ai";
        Assert.Equal("Main.cs", Assert.Single(panel.DisplayItems).Name);
    }

    [Fact]
    public async Task FilterDoesNotLookIntoSubfolders()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.SearchText = "numberbox";

        Assert.Empty(panel.DisplayItems);
        Assert.True(panel.ShowNoResults);
    }

    [Fact]
    public async Task RecursiveSearchFindsMatchesAcrossSubfoldersWithLocations()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "number";
        await WaitForSearch(panel, 3);

        var byName = panel.DisplayItems.ToDictionary(i => i.Name, i => i.RelativeFolder);
        Assert.Equal(Path.Combine("Recipe", "Controls"), byName["NumberBox.xaml"]);
        Assert.Equal("Views", byName["NumberBoxHost.cs"]);
        Assert.Equal("Recipe", byName["NumberFormat.cs"]);

        Assert.True(panel.IsRecursiveSearch);
        Assert.DoesNotContain("Untouched.txt", panel.DisplayItems.Select(i => i.Name));
    }

    [Fact]
    public async Task ItemsDirectlyUnderTheBaseHaveNoLocation()
    {
        using var f = new MainWindowFixture();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "main";
        await WaitForSearch(panel, 1);

        var hit = Assert.Single(panel.DisplayItems);
        Assert.Equal("Main.cs", hit.Name);
        Assert.False(hit.HasRelativeFolder);
        Assert.Equal(string.Empty, hit.RelativeFolderPrefix);
    }

    [Fact]
    public async Task ListViewPrefixesTheRelativeFolder()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "NumberBox.xaml";
        await WaitForSearch(panel, 1);

        var hit = Assert.Single(panel.DisplayItems);
        Assert.Equal(Path.Combine("Recipe", "Controls") + Path.DirectorySeparatorChar, hit.RelativeFolderPrefix);
        Assert.True(hit.HasRelativeFolder);
    }

    [Fact]
    public async Task SearchBaseIsTheCurrentPathNotTheAnchor()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "number";
        await WaitForSearch(panel, 3);
        Assert.Equal(3, panel.DisplayItems.Count);

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        Assert.Equal(string.Empty, panel.SearchText);

        panel.IncludeSubfolders = true;
        panel.SearchText = "number";
        await WaitForSearch(panel, 2);

        var names = panel.DisplayItems.Select(i => i.Name).ToList();
        Assert.Contains("NumberBox.xaml", names);
        Assert.Contains("NumberFormat.cs", names);
        Assert.DoesNotContain("NumberBoxHost.cs", names);
    }

    [Fact]
    public async Task NavigatingCancelsTheSearchAndClearsTheBox()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "number";
        await WaitForSearch(panel, 3);

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        Assert.Equal(string.Empty, panel.SearchText);
        Assert.False(panel.IsSearching);
        Assert.False(panel.HasSearchText);

        Assert.Equal(panel.Items.Count, panel.DisplayItems.Count);
        Assert.Contains("NumberBoxHost.cs", panel.DisplayItems.Select(i => i.Name));
    }

    [Fact]
    public async Task ClickingAnotherFolderMidSearchStopsItImmediately()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "n";

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.False(panel.IsSearching);
        Assert.Equal(string.Empty, panel.SearchText);
        Assert.Equal(f.DocsPath, panel.CurrentPath);
        Assert.Equal("spec.md", Assert.Single(panel.DisplayItems).Name);

        await Task.Delay(600);
        Assert.Equal("spec.md", Assert.Single(panel.DisplayItems).Name);
        Assert.False(panel.IsSearching);
    }

    [Fact]
    public async Task ChangingTheQueryCancelsThePreviousSearch()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "number";
        panel.SearchText = "numberformat";
        await WaitForSearch(panel, 1);

        Assert.Equal("NumberFormat.cs", Assert.Single(panel.DisplayItems).Name);
    }

    [Fact]
    public async Task TogglingIncludeSubfoldersReRunsTheSearch()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.SearchText = "numberbox";
        Assert.Empty(panel.DisplayItems);

        panel.IncludeSubfolders = true;
        await WaitForSearch(panel, 1);
        Assert.Equal(2, panel.DisplayItems.Count);

        panel.IncludeSubfolders = false;
        Assert.Empty(panel.DisplayItems);
        Assert.False(panel.IsSearching);
    }

    [Fact]
    public async Task NoResultsIsDistinctFromNotSearching()
    {
        using var f = new MainWindowFixture();
        var panel = await CodePanel(f);

        Assert.False(panel.ShowNoResults);

        panel.SearchText = "이런건없다";
        Assert.True(panel.ShowNoResults);
        Assert.Empty(panel.DisplayItems);

        panel.SearchText = string.Empty;
        Assert.False(panel.ShowNoResults);
    }

    [Fact]
    public async Task EmptyFolderWithoutSearchIsNotReportedAsNoResults()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var empty = Directory.CreateDirectory(Path.Combine(f.WorkPath, "EmptyDir")).FullName;
        await panel.NavigateToAsync(empty);

        Assert.Empty(panel.DisplayItems);
        Assert.False(panel.ShowNoResults);
    }

    [Fact]
    public async Task ReportsSkippedFoldersInsteadOfStalling()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));
        var panel = f.Rotating;

        panel.IncludeSubfolders = true;
        panel.SearchText = "log";

        for (var i = 0; i < 200 && (panel.IsSearching || panel.SearchSkippedFolders == 0); i++)
        {
            await Task.Delay(25);
        }

        Assert.False(panel.IsSearching);
        Assert.True(panel.HasSkippedFolders);
        Assert.Equal("1개 폴더 건너뜀", panel.SkippedFoldersText);
        Assert.True(panel.ShowNoResults);
    }

    [Fact]
    public async Task MetaCountFollowsWhatIsOnScreen()
    {
        using var f = new MainWindowFixture();
        var panel = await CodePanel(f);

        Assert.Contains($"{panel.Items.Count}개", panel.MetaText);

        panel.SearchText = "recipe";
        Assert.Contains("1개", panel.MetaText);
    }

    [Fact]
    public async Task SearchResultFolderCanBeOpenedAndPushesHistory()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        var panel = await CodePanel(f);

        panel.IncludeSubfolders = true;
        panel.SearchText = "controls";
        await WaitForSearch(panel, 1);

        var folder = Assert.Single(panel.DisplayItems);
        Assert.True(folder.IsDirectory);
        await panel.OpenAsync(folder);

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe", "Controls"), panel.CurrentPath);
        Assert.True(panel.CanGoBack);

        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
    }
}
