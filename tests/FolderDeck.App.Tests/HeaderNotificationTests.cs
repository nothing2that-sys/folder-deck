namespace FolderDeck.App.Tests;

/// <summary>
/// 재귀 검색 헤더 화살표 통지(Phase 12 · D69-h) — <c>IsRecursiveSearch</c> 가 바뀔 때 컬럼
/// 머리글 통지가 나가는지를 문다. §6-2 표의 5개를 정확히 그대로 옮긴다 — 더 만들지도
/// 합치지도 않는다.
/// </summary>
public sealed class HeaderNotificationTests
{
    // 1
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

    // 2
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

    // 3
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

    // 4
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

    // 5
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
