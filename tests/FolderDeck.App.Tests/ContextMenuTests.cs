namespace FolderDeck.App.Tests;

public sealed class ContextMenuTests
{
    [Fact]
    public async Task CopiesTheFullAbsolutePathOfAFile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.CopyItemPath(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        var copied = Assert.Single(f.Clipboard.Texts);
        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), copied);
        Assert.True(Path.IsPathFullyQualified(copied));
    }

    [Fact]
    public async Task CopiesTheFullPathOfAFolder()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CopyItemPath(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.Clipboard.Text);
    }

    [Fact]
    public async Task HeaderCopiesTheCurrentFolderPath()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.CopyCurrentPath();
        Assert.Equal(f.WorkPath, f.Clipboard.Text);

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        panel.CopyCurrentPath();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.Clipboard.Text);
    }

    [Fact]
    public async Task HeaderRevealsTheCurrentFolderInExplorer()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var current = Path.Combine(f.WorkPath, "Recipe");
        await panel.NavigateToAsync(current);

        panel.RevealCurrentFolderCommand.Execute(null);

        var revealed = Assert.Single(f.Shell.Revealed);
        Assert.Equal(current, revealed.Path);
        Assert.True(revealed.IsDirectory);
    }

    [Fact]
    public async Task RevealPassesTheFileWithItsDirectoryFlagFalse()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.RevealItem(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        var (path, isDirectory) = Assert.Single(f.Shell.Revealed);
        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), path);
        Assert.False(isDirectory);
    }

    [Fact]
    public async Task RevealPassesAFolderWithItsDirectoryFlagTrue()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.RevealItem(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var (path, isDirectory) = Assert.Single(f.Shell.Revealed);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), path);
        Assert.True(isDirectory);
    }

    [Fact]
    public async Task RevealDoesNotOpenTheFileItself()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.RevealItem(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        Assert.Empty(f.Shell.Opened);
    }

    [Fact]
    public async Task ClipboardFailureIsSurfacedNotSwallowed()
    {
        using var f = new MainWindowFixture();
        f.Clipboard.Error = "클립보드가 잠겨 있다";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.CopyItemPath(panel.DisplayItems[0]);

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("클립보드가 잠겨 있다", f.ViewModel.Message);
    }

    [Fact]
    public async Task RevealFailureIsSurfaced()
    {
        using var f = new MainWindowFixture();
        f.Shell.Error = "탐색기를 실행할 수 없다";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.RevealItem(panel.DisplayItems[0]);

        Assert.Contains("탐색기를 실행할 수 없다", f.ViewModel.Message);
    }

    [Fact]
    public void NullItemIsIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        panel.CopyItemPath(null);
        panel.RevealItem(null);
        panel.CopyCurrentPath();
        panel.RevealCurrentFolderCommand.Execute(null);

        Assert.Empty(f.Clipboard.Texts);
        Assert.Empty(f.Shell.Revealed);
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

        panel.CopyItemPath(Assert.Single(panel.DisplayItems));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml"), f.Clipboard.Text);
    }
}
