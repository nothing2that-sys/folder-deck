namespace FolderDeck.App.Tests;

public sealed class OpenAndPropertiesTests
{

    [Fact]
    public async Task OpenOnAFolderDescendsInsideTheTile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
        Assert.True(panel.IsAwayFromAnchor);

        Assert.Empty(f.Shell.Revealed);
        Assert.Empty(f.Shell.Opened);
    }

    [Fact]
    public async Task OpenOnAFileGoesToTheShellNotToExplorer()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), Assert.Single(f.Shell.Opened));
        Assert.Empty(f.Shell.Revealed);

        Assert.Equal(f.DocsPath, panel.CurrentPath);
    }

    [Fact]
    public async Task OpenNeverTouchesTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(f.WorkPath, panel.Entry!.Path);
        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
    }

    [Fact]
    public async Task PropertiesPassesTheFilePathToTheShell()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), Assert.Single(f.Shell.Properties));
    }

    [Fact]
    public async Task PropertiesWorksOnAFolderToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), Assert.Single(f.Shell.Properties));
    }

    [Fact]
    public async Task PropertiesChangesNothingElse()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Empty(f.Shell.Opened);
        Assert.Empty(f.Shell.Revealed);
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.Equal(f.WorkPath, panel.Entry!.Path);
    }

    [Fact]
    public async Task PropertiesFailureIsSurfacedNotSwallowed()
    {
        using var f = new MainWindowFixture();
        f.Shell.Error = "속성 창을 열 수 없다";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems[0]);

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("속성 창을 열 수 없다", f.ViewModel.Message);
    }

    [Fact]
    public void NullItemIsIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        panel.ShowItemProperties(null);

        Assert.Empty(f.Shell.Properties);
    }

    [Fact]
    public async Task WorksOnSearchResultsToo()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.IncludeSubfolders = true;
        panel.SearchText = "NumberBox.xaml";
        for (var i = 0; i < 200 && (panel.IsSearching || panel.DisplayItems.Count == 0); i++)
        {
            await Task.Delay(25);
        }

        panel.ShowItemProperties(Assert.Single(panel.DisplayItems));

        Assert.Equal(
            Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml"),
            Assert.Single(f.Shell.Properties));
    }
}
