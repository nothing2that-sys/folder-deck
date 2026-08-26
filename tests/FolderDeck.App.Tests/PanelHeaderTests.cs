using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 폴더 패널 헤더 — 1줄에 이름·개수·아이콘, 2줄은 있을 때만 (Phase 8-C ③).
/// </summary>
/// <remarks>
/// 개수·뷰 방식·정렬이 2줄째 <b>글자</b>에서 1줄 <b>아이콘</b>으로 옮겨졌다. 값을 만드는 자리는
/// 늘지 않았다 — 아래 판들이 확인하는 것은 새 조각들이 기존 <c>ViewModeText</c>·<c>SortLabel</c>·
/// <c>SortArrow</c>·<c>FoldersFirst</c> 와 <b>같은 사실</b>을 말한다는 것이다.
/// </remarks>
public sealed class PanelHeaderTests
{
    // ── 개수 ─────────────────────────────────────────────────────────────────

    /// <summary>1줄에 붙는 개수는 구분점이 없다 — 뒤에 이어 붙일 조각이 더 없다.</summary>
    [Fact]
    public async Task TheCountOnTheNameLineCarriesNoSeparator()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Equal("3개", code.ItemCountText);
        Assert.Equal("3개 · ", code.MetaCountText);
    }

    /// <summary>
    /// 메타 줄의 규약은 그대로다 — <see cref="FolderPanelViewModel.MetaText"/> 는 세 조각을
    /// 이어 붙인 것이고, 지금은 개수 툴팁으로 화면에 남는다.
    /// </summary>
    [Fact]
    public async Task TheMetaLineStillJoinsAndStillAgreesWithTheCount()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
        Assert.StartsWith(code.ItemCountText, code.MetaText);
    }

    [Fact]
    public async Task TheCountFollowsTheList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.NavigateToCommand.ExecuteAsync(System.IO.Path.Combine(f.CodePath, "Recipe"));

        Assert.Contains(nameof(FolderPanelViewModel.ItemCountText), seen);
        Assert.Equal("1개", code.ItemCountText);
    }

    // ── 선택 조각 (Phase 8-D ①) ──────────────────────────────────────────────

    /// <summary>안 골랐으면 빈 문자열이다 — 구분점만 뜬 꼬리가 남지 않게.</summary>
    [Fact]
    public async Task NothingSelectedMeansNoSuffix()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.False(code.HasSelection);
        Assert.Equal(string.Empty, code.SelectionSuffix);
    }

    /// <summary>
    /// 구분점은 이 조각이 들고 온다 — 앞의 개수와 한 줄로 붙기 때문이다.
    /// 문구 자체는 <see cref="FolderPanelViewModel.SelectionText"/> 그대로다(새로 만들지 않았다).
    /// </summary>
    [Fact]
    public async Task TheSuffixCarriesItsOwnSeparator()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [.. code.Items.Take(2)];

        Assert.Equal("2개 선택", code.SelectionText);
        Assert.Equal(" · 2개 선택", code.SelectionSuffix);

        // 화면에 그려지는 줄 = 개수 + 이 조각.
        Assert.Equal("3개 · 2개 선택", code.ItemCountText + code.SelectionSuffix);
    }

    [Fact]
    public async Task TheSuffixIsAnnouncedWhenTheSelectionChanges()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.SelectedItems = [code.Items[0]];

        Assert.Contains(nameof(FolderPanelViewModel.SelectionSuffix), seen);
    }

    /// <summary>
    /// 선택을 풀면 조각도 사라진다 — 줄이 생겼다 없어지는 것이 아니라 글자만 짧아진다.
    /// </summary>
    [Fact]
    public async Task ClearingTheSelectionShortensTheLineInsteadOfRemovingIt()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [code.Items[0]];
        Assert.NotEqual(string.Empty, code.SelectionSuffix);
        Assert.False(code.HasStatusLine);

        code.ClearSelection();

        Assert.Equal(string.Empty, code.SelectionSuffix);
        Assert.False(code.HasStatusLine);
    }

    /// <summary>
    /// 앵커 밖은 2줄에 그대로 남는다 — 고르는 동작과 무관하게 바뀌므로 손 아래에서 뛰지 않는다.
    /// </summary>
    [Fact]
    public async Task LeavingTheAnchorStillUsesTheSecondLine()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.NavigateToCommand.ExecuteAsync(System.IO.Path.Combine(f.CodePath, "Recipe"));

        Assert.True(code.IsAwayFromAnchor);
        Assert.True(code.HasStatusLine);

        // 거기서 골라도 줄 수는 그대로다.
        code.SelectedItems = [code.Items[0]];
        Assert.True(code.HasStatusLine);
    }

    // ── 뷰 방식 아이콘 ───────────────────────────────────────────────────────

    /// <summary>list 는 줄 셋, details 는 격자. 유니코드 글리프만 쓴다 (§1.2).</summary>
    [Fact]
    public async Task TheViewModeIconSaysWhichViewIsOn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.IsDetailsView);
        Assert.Equal("▦", code.ViewModeIcon);

        code.ToggleViewModeCommand.Execute(null);

        Assert.True(code.IsListView);
        Assert.Equal("☰", code.ViewModeIcon);
    }

    /// <summary>아이콘만으로는 무엇이 될지 모른다 — 툴팁이 지금과 다음을 둘 다 적는다.</summary>
    [Fact]
    public async Task TheViewModeTipNamesBothStates()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Contains("지금 details", code.ViewModeTip);
        Assert.Contains("list", code.ViewModeTip);

        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains("지금 list", code.ViewModeTip);
        Assert.Contains("details", code.ViewModeTip);
    }

    [Fact]
    public async Task TheViewModeIconIsAnnouncedWhenItFlips()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains(nameof(FolderPanelViewModel.ViewModeIcon), seen);
        Assert.Contains(nameof(FolderPanelViewModel.ViewModeTip), seen);
    }

    // ── 정렬 아이콘 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 방향은 삼각형이다 — 같은 아이콘 줄의 「상위 폴더 ↑」와 섞이지 않게 화살표를 피했다.
    /// </summary>
    [Fact]
    public async Task TheSortIconIsADirectionTriangle()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.Equal("▴", f.ViewModel.Panels[0].SortIcon);

        // 산출물 칸은 크기 내림차순으로 시작한다.
        Assert.Equal("▾", f.ViewModel.Panels[1].SortIcon);
    }

    [Fact]
    public async Task TheSortIconFlipsWithTheDirection()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Equal("▴", code.SortIcon);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Equal("▾", code.SortIcon);
    }

    /// <summary>
    /// <b>통합 정렬이 갈 자리가 아이콘 앞의 <c>∪</c> 다.</b> 2줄째 " · 통합"이 그 줄과 함께
    /// 사라졌고, 저장되지 않는 상태라 화면이 말해 주지 않으면 알 길이 없다.
    /// </summary>
    [Fact]
    public async Task TurningOffFoldersFirstShowsUpOnTheSortIcon()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal("▴", code.SortIcon);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal("∪▴", code.SortIcon);
        Assert.Contains("통합", code.SortTip);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal("▴", code.SortIcon);
        Assert.DoesNotContain("통합", code.SortTip);
    }

    /// <summary>통합 표시와 방향은 서로를 지우지 않는다 — 둘 다 붙는다.</summary>
    [Fact]
    public async Task TheMergedMarkAndTheDirectionBothShow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        Assert.True(output.Entry!.SortDesc);

        await output.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal("∪▾", output.SortIcon);
    }

    /// <summary>
    /// 아이콘이 못 담는 <b>기준</b>은 툴팁이 적는다 — 네 값에 글리프를 하나씩 붙이면
    /// 어휘표가 이 앱 쪽으로 비대해진다(familylook §6).
    /// </summary>
    [Fact]
    public async Task TheSortTipNamesTheCriterion()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Contains("이름 ↑", code.SortTip);

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.Contains("수정 ↑", code.SortTip);

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.Contains("수정 ↓", code.SortTip);
    }

    /// <summary>아이콘과 메타 문구는 같은 재료에서 나온다 — 어긋날 수 없다.</summary>
    [Fact]
    public async Task TheIconAndTheMetaTextAgree()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        foreach (var sort in new[] { SortBy.Name, SortBy.Type, SortBy.Type, SortBy.Size })
        {
            await code.SetSortCommand.ExecuteAsync(sort);

            var descending = code.MetaSortText.Contains('↓');
            Assert.Equal(descending ? "▾" : "▴", code.SortIcon);
        }
    }

    [Fact]
    public async Task TheSortIconIsAnnouncedWhenItChanges()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Contains(nameof(FolderPanelViewModel.SortIcon), seen);
        Assert.Contains(nameof(FolderPanelViewModel.SortTip), seen);

        seen.Clear();
        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        Assert.Contains(nameof(FolderPanelViewModel.SortIcon), seen);
        Assert.Contains(nameof(FolderPanelViewModel.SortTip), seen);
    }

    // ── 2줄째: 있을 때만 ─────────────────────────────────────────────────────

    /// <summary>평소에는 뜰 것이 하나도 없다 — 그때 줄을 접어 헤더 높이를 돌려준다.</summary>
    [Fact]
    public async Task TheStatusLineIsGoneWhenNothingIsHappening()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.False(code.IsAwayFromAnchor);
        Assert.False(code.HasSelection);
        Assert.False(code.IsLoading);
        Assert.False(code.HasStatusLine);
    }

    /// <summary>
    /// <b>고른다고 2줄이 생기지 않는다</b> (Phase 8-D ①). 이 줄은 있다 없다 하는 줄이라,
    /// 고르는 순간 헤더가 한 줄 자라고 풀면 줄어 <b>목록이 손 아래에서 뛰었다</b>.
    /// 선택은 늘 있는 1줄로 올라갔다 — <see cref="FolderPanelViewModel.SelectionSuffix"/>.
    /// </summary>
    [Fact]
    public async Task SelectingSomethingDoesNotChangeTheHeaderHeight()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.HasStatusLine);

        code.SelectedItems = [code.Items[0]];

        Assert.True(code.HasSelection);
        Assert.False(code.HasStatusLine);

        code.SelectedItems = [];
        Assert.False(code.HasStatusLine);
    }

    [Fact]
    public async Task LeavingTheAnchorBringsTheStatusLineBack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.HasStatusLine);

        await code.NavigateToCommand.ExecuteAsync(System.IO.Path.Combine(f.CodePath, "Recipe"));

        Assert.True(code.IsAwayFromAnchor);
        Assert.True(code.HasStatusLine);

        await code.GoToAnchorCommand.ExecuteAsync(null);

        Assert.False(code.HasStatusLine);
    }

    /// <summary>읽는 중에도 뜬다 — 그 셋이 이 줄의 전부다.</summary>
    [Fact]
    public void LoadingBringsTheStatusLineBack()
    {
        using var f = new MainWindowFixture();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.IsLoading = true;

        Assert.True(code.HasStatusLine);
        Assert.Contains(nameof(FolderPanelViewModel.HasStatusLine), seen);
    }
}
