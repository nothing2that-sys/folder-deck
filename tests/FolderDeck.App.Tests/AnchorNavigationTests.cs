namespace FolderDeck.App.Tests;


public sealed class AnchorNavigationTests
{
    [Fact]
    public async Task DescendingUpdatesCurrentPathButNotTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
        Assert.True(panel.IsAwayFromAnchor);


        Assert.Equal(f.WorkPath, panel.Entry!.Path);
        Assert.Equal(f.WorkPath, f.Workspace.Folders.Single(x => x.DisplayName == "작업").Path);
        Assert.Equal("Recipe.cs", Assert.Single(panel.Items).Name);
    }

    [Fact]
    public async Task BreadcrumbFoldsTheAnchorAndAppendsSubfolders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;


        var first = Assert.Single(panel.Breadcrumbs);
        Assert.Equal("작업", first.Name);
        Assert.True(first.IsAnchor);

        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));

        Assert.Equal(["작업", "Recipe"], panel.Breadcrumbs.Select(b => b.Name));
        Assert.True(panel.Breadcrumbs[0].IsAnchor);
        Assert.False(panel.Breadcrumbs[1].IsAnchor);
        Assert.Equal(f.WorkPath, panel.Breadcrumbs[0].Path);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.Breadcrumbs[1].Path);
    }

    [Fact]
    public async Task LeftRowShowsWhereThePanelWanderedTo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        Assert.Null(f.Row("작업").LocationHint);

        var panel = f.Rotating;
        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));

        Assert.Equal("지금 작업 › Recipe 안", f.Row("작업").LocationHint);
        Assert.True(f.Row("작업").HasLocationHint);
    }




    [Fact]
    public async Task ReturnsToAnchorViaBreadcrumbFirstSegment()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await panel.NavigateToAsync(panel.Breadcrumbs[0].Path);

        AssertAtAnchor(f, panel);
    }


    [Fact]
    public async Task ReturnsToAnchorViaReClickingTheSameRow()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        AssertAtAnchor(f, panel);
    }


    [Fact]
    public async Task ReturnsToAnchorViaAltHomeCommand()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await panel.GoToAnchorCommand.ExecuteAsync(null);

        AssertAtAnchor(f, panel);
    }

    [Fact]
    public async Task GoUpLeavesTheAnchorWhenAlreadyAtIt()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;


        await panel.GoUpAsync();

        Assert.Equal(Directory.GetParent(f.WorkPath)!.FullName, panel.CurrentPath);
        Assert.True(panel.IsAwayFromAnchor);


        Assert.False(panel.Breadcrumbs[0].IsAnchor);
        Assert.StartsWith("앵커 밖", panel.LocationHint);

        await panel.GoToAnchorAsync();
        AssertAtAnchor(f, panel);
    }

    [Fact]
    public async Task GoUpFromASubfolderComesBackToTheAnchor()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await panel.GoUpAsync();

        AssertAtAnchor(f, panel);
    }

    [Fact]
    public async Task CannotGoAboveADriveRoot()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var root = Path.GetPathRoot(f.WorkPath)!;
        await panel.NavigateToAsync(root);

        Assert.False(panel.CanGoUp);
        await panel.GoUpAsync();
        Assert.Equal(root, panel.CurrentPath);
    }





    [Fact]
    public async Task CurrentPathIsNeverPersisted()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);
        var subPath = panel.CurrentPath;


        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        f.Store.Flush();

        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));
        Assert.DoesNotContain("currentPath", json);
        Assert.DoesNotContain("Recipe", json);


        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var anchor = reloaded.Folders.Single(x => x.DisplayName == "작업").Path;
        Assert.Equal(f.WorkPath, anchor);
        Assert.NotEqual(subPath, anchor);
    }

    [Fact]
    public async Task PinnedPanelKeepsItsOwnPositionIndependentOfTheRotatingOne()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var pinned = f.ViewModel.Panels[0];


        await pinned.OpenAsync(pinned.Items.Single(i => i.Name == "Views"));
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal(Path.Combine(f.CodePath, "Views"), pinned.CurrentPath);
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);

        await pinned.GoToAnchorAsync();
        Assert.Equal(f.CodePath, pinned.CurrentPath);
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
    }

    private static async Task<ViewModels.FolderPanelViewModel> DescendedRotatingPanel(MainWindowFixture f)
    {
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));
        Assert.True(panel.IsAwayFromAnchor);
        return panel;
    }

    private static void AssertAtAnchor(MainWindowFixture f, ViewModels.FolderPanelViewModel panel)
    {
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);
        Assert.Null(panel.LocationHint);
        Assert.True(Assert.Single(panel.Breadcrumbs).IsAnchor);
        Assert.Null(f.Row("작업").LocationHint);
    }
}
