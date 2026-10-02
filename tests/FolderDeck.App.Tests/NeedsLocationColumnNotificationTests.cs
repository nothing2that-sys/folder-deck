using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;







public sealed class NeedsLocationColumnNotificationTests
{

    private static FolderPanelViewModel BareSearchPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: null);

    private static async Task WaitForSearchToSettle(FolderPanelViewModel panel)
    {
        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }
    }


    [Fact]
    public async Task TurningOnRecursiveSearchNotifiesNeedsLocationColumn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.IncludeSubfolders = true;

        Assert.Contains(nameof(FolderPanelViewModel.NeedsLocationColumn), seen);

        await WaitForSearchToSettle(panel);
    }


    [Fact]
    public async Task ClearingTheSearchTextNotifiesNeedsLocationColumn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);
        Assert.True(panel.NeedsLocationColumn);

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.SearchText = string.Empty;

        Assert.Contains(nameof(FolderPanelViewModel.NeedsLocationColumn), seen);
        Assert.False(panel.NeedsLocationColumn);
    }


    [Fact]
    public async Task TurningOffIncludeSubfoldersAloneNotifiesNeedsLocationColumn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);
        Assert.True(panel.NeedsLocationColumn);

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.IncludeSubfolders = false;

        Assert.Contains(nameof(FolderPanelViewModel.NeedsLocationColumn), seen);
        Assert.False(panel.NeedsLocationColumn);
    }


    [Fact]
    public async Task NeedsLocationColumnTransitionsFalseToTrueAndBack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        Assert.False(panel.NeedsLocationColumn);

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);

        Assert.True(panel.NeedsLocationColumn);

        panel.SearchText = string.Empty;

        Assert.False(panel.NeedsLocationColumn);
    }






    [Fact]
    public void SearchTilesNeedTheLocationColumnFromTheStart()
    {
        var panel = BareSearchPanel();

        Assert.True(panel.NeedsLocationColumn);
    }
}
