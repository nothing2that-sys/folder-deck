using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 검색 타일 컬럼 머리(Phase 12 · D69-g) — 헤더를 눌러도 안 먹던 것을 고친 자리와, 그
/// 과정에서 재귀 검색·빈 패널이 깨지지 않는지를 함께 문다. §6-3 표의 8개를 정확히 그대로
/// 옮긴다 — 더 만들지도 합치지도 않는다.
/// </summary>
public sealed class SearchColumnHeaderTests
{
    private static FolderPanelViewModel MakePanel(bool isSearchTile) => new(
        new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
        isRotating: false, isSearchTile);

    // ── 검색 타일 헤더(1~3) ──────────────────────────────────────────────────

    // 1
    [Fact]
    public void SearchTileNameHeaderShowsAnArrowByDefault()
    {
        var panel = MakePanel(isSearchTile: true);

        Assert.Equal("이름 ↑", panel.NameHeaderText);
    }

    // 2
    [Fact]
    public async Task SettingSortToSizeMovesTheArrowToTheSizeHeader()
    {
        var panel = MakePanel(isSearchTile: true);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal("크기 ↑", panel.SizeHeaderText);
        Assert.Equal("이름", panel.NameHeaderText);
    }

    // 3
    [Fact]
    public async Task FlippingTheDirectionChangesTheArrowShape()
    {
        var panel = MakePanel(isSearchTile: true);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        await panel.SetSortCommand.ExecuteAsync(SortBy.Size); // 같은 기준 재클릭 = 방향 뒤집기

        Assert.Equal("크기 ↓", panel.SizeHeaderText);
    }

    // ── 「깨졌나」(4~6) — 빼지 마라 ───────────────────────────────────────────

    // 4 — §6-1 이 코드를 고치기 전에 재라고 한 바로 그 확인이다(보고문 §3 참고).
    [Fact]
    public async Task RecursiveSearchShowsNoArrowOnAnyHeader()
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

        Assert.True(panel.IsRecursiveSearch);
        Assert.Equal("이름", panel.NameHeaderText);
        Assert.Equal("크기", panel.SizeHeaderText);
        Assert.Equal("수정", panel.ModifiedHeaderText);
    }

    // 5
    [Fact]
    public async Task AnOrdinaryFolderListStillShowsArrowsAsBefore()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var panel = f.ViewModel.Panels[0];

        Assert.Equal("이름 ↑", panel.NameHeaderText);
    }

    // 6 — D69-f 가 낸 회귀를 그 자리에서 고친 자리다. 이번 회차가 같은 자원을 다시 만지므로
    // 되짚는다.
    [Fact]
    public void AnEmptyRotatingPanelStillHasPlainHeaders()
    {
        using var f = new MainWindowFixture();
        var rotating = f.Rotating;

        Assert.True(rotating.IsEmpty);
        Assert.Equal("이름", rotating.NameHeaderText);
        Assert.Equal("크기", rotating.SizeHeaderText);
        Assert.Equal("수정", rotating.ModifiedHeaderText);
    }

    // ── ColumnSort.GetSortKey 의 뜻을 뷰모델 쪽에서 문다(7~8) — §5 참고 ─────────

    // 7 — XAML 자원을 여는 길이 이 시험 프로젝트에 없어(§6-3 안내) 같은 뜻을 뷰모델 쪽에서
    // 문다: 컬럼마다 자기 SortBy 로만 화살표를 받고 나머지 둘은 안 받는지 — 이것이
    // SearchGridView 의 이름·크기·수정 컬럼에 붙는 v:ColumnSort.SortKey 가 실제로 하는 일이다.
    // 세 기준을 한 시험 안에서 차례로 확인한다 — [Theory] 로 쪼개면 xUnit 이 셋으로 세어
    // 「정확히 8개」 게이트가 어긋난다.
    [Fact]
    public async Task EachColumnOnlyGetsItsOwnArrow()
    {
        static bool HasArrow(string text) => text.Contains('↑') || text.Contains('↓');

        var panel = MakePanel(isSearchTile: true);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Name);
        Assert.True(HasArrow(panel.NameHeaderText));
        Assert.False(HasArrow(panel.SizeHeaderText));
        Assert.False(HasArrow(panel.ModifiedHeaderText));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.False(HasArrow(panel.NameHeaderText));
        Assert.True(HasArrow(panel.SizeHeaderText));
        Assert.False(HasArrow(panel.ModifiedHeaderText));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.False(HasArrow(panel.NameHeaderText));
        Assert.False(HasArrow(panel.SizeHeaderText));
        Assert.True(HasArrow(panel.ModifiedHeaderText));
    }

    // 8 — XAML 자원을 여는 길이 없어 같은 뜻을 뷰모델 쪽에서 문다: 「위치」에 눌러서 정렬할
    // 자리를 안 준다는 결정(2)은 SortBy 열거자에 경로 값이 없다는 사실 하나로 지켜진다 —
    // 그 사실 자체를 고정한다(§9 정지 조건 7 을 어기면 이 시험이 깨진다).
    [Fact]
    public void SortByHasNoLocationOrPathMember()
    {
        var names = Enum.GetNames<SortBy>();

        Assert.Equal(["Name", "Modified", "Size", "Type"], names);
    }
}
