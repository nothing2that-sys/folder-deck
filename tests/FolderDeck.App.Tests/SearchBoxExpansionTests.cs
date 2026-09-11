namespace FolderDeck.App.Tests;

/// <summary>검색창 접힘/펴짐 — 런타임 전용 순간 UI 상태(저장 안 함).</summary>
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

    /// <summary>결과를 보는 중에 접히면 무엇을 보고 있는지 알 수 없게 된다.</summary>
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

    /// <summary>
    /// 저장 대상이 아니다 — 왼쪽 컬럼 접기(<see cref="LeftLayoutTests"/>)와 반대다.
    /// </summary>
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

        // 새로 띄우면(= 재실행) 항상 접힌 기본에서 시작한다.
        using var restarted = new MainWindowFixture();
        Assert.All(restarted.ViewModel.Panels, p => Assert.False(p.IsSearchExpanded));
    }

    /// <summary>폴더를 옮기면 검색어가 지워지므로 그 뒤에는 접힐 수 있다.</summary>
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

    // ── ✕ / 🔍 토글로 명시적으로 닫기 ────────────────────────────────────────

    /// <summary>
    /// ✕ 와 🔍 토글은 검색어가 있어도 닫는다 — 포커스 이탈(닫지 않음)과 다른 규칙이다.
    /// 닫으면서 검색어도 비워 목록이 원래대로 돌아온다.
    /// </summary>
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

    /// <summary>
    /// 「더 보기」 메뉴의 「검색」 체크 항목이 부르는 토글(Phase 20 · D119 · T13) — 헤더의
    /// 예전 🔍 아이콘 클릭(코드비하인드 <c>OnSearchToggleClick</c>)과 같은 판정을 이제
    /// 커맨드로 직접 부를 수 있어야 한다.
    /// </summary>
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

    /// <summary>재귀 검색 중에 닫아도 검색이 취소되고 목록이 돌아온다.</summary>
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
