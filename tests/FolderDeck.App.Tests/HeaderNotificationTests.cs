namespace FolderDeck.App.Tests;






public sealed class HeaderNotificationTests
{

    [Fact]
    public async Task SwitchingOnRecursiveSearchNotifiesTheNameHeader()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.IncludeSubfolders = true;

        Assert.Contains(nameof(FolderPanelViewModel.NameHeaderText), seen);

        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }
    }


    [Fact]
    public async Task SwitchingOnRecursiveSearchClearsTheArrow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var before = panel.NameHeaderText;
        Assert.True(before.Contains('↑') || before.Contains('↓'));

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;

        Assert.True(panel.IsRecursiveSearch);
        Assert.Equal("이름", panel.NameHeaderText);
        Assert.NotEqual(before, panel.NameHeaderText);

        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }
    }


    [Fact]
    public async Task TurningSearchOffBringsTheArrowBack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }
        Assert.True(panel.IsRecursiveSearch);

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.SearchText = string.Empty;

        Assert.Contains(nameof(FolderPanelViewModel.NameHeaderText), seen);
        Assert.False(panel.IsRecursiveSearch);
        Assert.Equal("이름 ↑", panel.NameHeaderText);
    }


    [Fact]
    public async Task TogglingIncludeSubfoldersAloneNotifiesEachTime()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.IncludeSubfolders = true;
        Assert.Contains(nameof(FolderPanelViewModel.NameHeaderText), seen);
        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }

        seen.Clear();
        panel.IncludeSubfolders = false;
        Assert.Contains(nameof(FolderPanelViewModel.NameHeaderText), seen);
    }


    [Fact]
    public async Task UnrelatedChangeDoesNotNotifyHeaders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.CurrentPath = f.CodePath;

        Assert.DoesNotContain(nameof(FolderPanelViewModel.NameHeaderText), seen);
        Assert.DoesNotContain(nameof(FolderPanelViewModel.SizeHeaderText), seen);
        Assert.DoesNotContain(nameof(FolderPanelViewModel.ModifiedHeaderText), seen);
    }
}
