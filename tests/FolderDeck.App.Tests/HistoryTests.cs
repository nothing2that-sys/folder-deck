namespace FolderDeck.App.Tests;


public sealed class HistoryTests
{
    [Fact]
    public async Task NothingToGoBackToAtTheStart()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var pinned = f.ViewModel.Panels[0];
        Assert.False(pinned.CanGoBack);
        Assert.False(pinned.CanGoForward);
        Assert.False(pinned.GoBackCommand.CanExecute(null));


        await pinned.GoBackAsync();
        Assert.Equal(f.CodePath, pinned.CurrentPath);
    }


    [Fact]
    public async Task TwoLevelsDownThenTwiceBackReturnsToStart()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));
        await panel.OpenAsync(panel.DisplayItems.Single(i => i.Name == "Controls"));
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe", "Controls"), panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);
    }

    [Fact]
    public async Task ForwardGoesBackToWhereYouCameFrom()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));
        await panel.GoBackAsync();
        Assert.True(panel.CanGoForward);

        await panel.GoForwardAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
        Assert.False(panel.CanGoForward);
    }




    [Fact]
    public async Task BackIsNotTheSameAsUp()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        var views = Path.Combine(f.WorkPath, "Views");

        await panel.NavigateToAsync(recipe);
        await panel.NavigateToAsync(views);
        Assert.Equal(views, panel.CurrentPath);

        await panel.GoBackAsync();


        Assert.Equal(recipe, panel.CurrentPath);
        Assert.NotEqual(f.WorkPath, panel.CurrentPath);
    }

    [Fact]
    public async Task GoingSomewhereNewClearsTheForwardStack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        await panel.GoBackAsync();
        Assert.True(panel.CanGoForward);

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        Assert.False(panel.CanGoForward);
        Assert.True(panel.CanGoBack);
    }

    [Fact]
    public async Task UpAndAnchorReturnAreBothPushed()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        await panel.GoUpAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);

        await panel.GoToAnchorAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
    }




    [Fact]
    public async Task BackUndoesSwitchingFoldersFromTheLeftList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var panel = f.Rotating;

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Equal(f.DocsPath, panel.CurrentPath);

        await panel.GoBackAsync();


        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.Same(f.Row("작업").Entry, panel.Entry);
        Assert.True(panel.IsDetailsView);
    }


    [Fact]
    public async Task ReturningToTheSamePlaceIsNotPushed()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;


        Assert.False(panel.CanGoBack);

        await panel.GoToAnchorAsync();
        await panel.GoToAnchorAsync();

        Assert.False(panel.CanGoBack);


        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
    }

    [Fact]
    public async Task EachPanelKeepsItsOwnHistory()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var pinned = f.ViewModel.Panels[0];
        var rotating = f.Rotating;

        await pinned.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));
        Assert.True(pinned.CanGoBack);
        Assert.False(rotating.CanGoBack);

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        await rotating.GoBackAsync();


        Assert.Equal(Path.Combine(f.WorkPath, "Views"), pinned.CurrentPath);
    }

    [Fact]
    public async Task HistoryIsNotPersisted()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        Assert.True(panel.CanGoBack);

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        f.Store.Flush();
        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recipe", json);


        using var restarted = new MainWindowFixture();
        await restarted.ViewModel.InitializeAsync();
        Assert.False(restarted.ViewModel.Panels[0].CanGoBack);
        Assert.False(restarted.Rotating.CanGoBack);
    }

    [Fact]
    public async Task RefreshDoesNotTouchHistory()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));

        await f.ViewModel.RefreshAllAsync();

        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
    }
}
