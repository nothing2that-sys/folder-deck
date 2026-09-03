using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

/// <summary>
/// <c>NeedsLocationColumn</c> 변경 통지(Phase 15 · D80 B-0 · D80-a) — 값 계산
/// (<c>IsRecursiveSearch || IsSearchTile</c>)은 항상 옳았지만 변경 통지가 어디에도 없어서
/// 컬럼 메뉴의 `위치` 항목과 <c>DetailsList</c> 의 뷰 전환이 재귀 검색을 켜도 따라가지
/// 못했다(D80 B-0 §2 Q3·Q4, JH 손 확인으로 뷰 전환도 확인됨).
/// </summary>
public sealed class NeedsLocationColumnNotificationTests
{
    /// <summary>패널만 떼어 만든다 — <see cref="ColumnVisibilityTests"/> 의 <c>BareSearchPanel</c> 과 같은 결.</summary>
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

    // 1
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

    // 2 (검색어를 비우는 경로)
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

    // 2번의 다른 경로 — "하위 포함" 을 직접 끄는 쪽. 검색어는 남겨 둔다.
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

    // 3
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

    // 4
    /// <summary>
    /// 검색 타일은 <c>IsSearchTile</c> 이 생성자에서 정해져 그 뒤 절대 안 바뀐다 — 통지와
    /// 무관하게 처음부터 참이다.
    /// </summary>
    [Fact]
    public void SearchTilesNeedTheLocationColumnFromTheStart()
    {
        var panel = BareSearchPanel();

        Assert.True(panel.NeedsLocationColumn);
    }
}
