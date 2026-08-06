using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일 헤더 메타의 정렬 글자를 눌러 <c>sortBy</c> 를 바꾼다 (§3.2 · §7.1 <c>folders[].sortBy</c>).
/// 그 전까지 정렬 엔진은 완비였는데 값을 바꿀 길이 없어 JSON 을 손으로 고쳐야 했다(부채 17).
/// </summary>
/// <remarks>
/// 본보기는 <see cref="ViewModeToggleTests"/> 다. <b>다른 점 하나가 이 판들의 핵심이다</b>:
/// 뷰 방식 전환은 선택을 비우지만 <b>정렬 변경은 선택을 잇는다</b> — 아래 네 번째 판이 그것을 지킨다.
/// </remarks>
public sealed class SortEntryPointTests
{
    [Fact]
    public async Task ChoosingACriterionChangesItAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(SortBy.Name, code.Entry!.SortBy);

        var saves = Count(code);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(SortBy.Size, code.Entry.SortBy);

        // 뷰 토글과 같은 규칙 — 한 번 고르면 저장도 한 번이다.
        Assert.Equal(1, saves());
    }

    /// <summary>
    /// 같은 기준을 다시 고르면 방향이 뒤집힌다 — 4값 + 방향을 한 글자로 순환시키면 8단계가 된다.
    /// </summary>
    [Fact]
    public async Task ChoosingTheSameCriterionFlipsTheDirection()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        // 산출물 칸은 크기 내림차순으로 시작한다 — 큰 것이 위다.
        var output = f.ViewModel.Panels[1];
        Assert.Equal(SortBy.Size, output.Entry!.SortBy);
        Assert.True(output.Entry.SortDesc);
        Assert.Equal(["b.dll", "a.dll"], output.Items.Select(i => i.Name));

        await output.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal(SortBy.Size, output.Entry.SortBy);
        Assert.False(output.Entry.SortDesc);

        // 목록이 실제로 다시 읽혀 뒤집혔다 — 문구만 바뀐 것이 아니다.
        Assert.Equal(["a.dll", "b.dll"], output.Items.Select(i => i.Name));
        Assert.Equal(" · 크기 ↑", output.MetaSortText);
    }

    /// <summary>
    /// 다른 기준으로 옮기면 오름차순으로 되돌린다 — 크기 내림차순을 보다가 이름으로 옮겼는데
    /// 이름이 거꾸로 서 있으면 놀란다.
    /// </summary>
    [Fact]
    public async Task ChoosingADifferentCriterionResetsToAscending()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        Assert.True(output.Entry!.SortDesc);

        await output.SetSortCommand.ExecuteAsync(SortBy.Name);

        Assert.Equal(SortBy.Name, output.Entry.SortBy);
        Assert.False(output.Entry.SortDesc);
        Assert.Equal(" · 이름 ↑", output.MetaSortText);
    }

    /// <summary>
    /// <b>고른 파일은 정렬을 바꿔도 살아남는다</b> (§4.8). 뷰 방식 전환과 갈리는 자리다 —
    /// 거기서는 두 <c>ListView</c> 의 <c>Visibility</c> 가 갈리지만 여기서는 갈리지 않는다.
    /// 목록을 다시 읽는 길(<c>RefreshAsync</c>)이 경로로 다시 잡아 준다.
    /// </summary>
    [Fact]
    public async Task SelectionSurvivesASortChange()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        var chosen = output.Items.Single(i => i.Name == "a.dll");
        output.SelectedItems = [chosen];
        Assert.Same(output, f.ViewModel.SelectionOwner);

        var cleared = 0;
        output.SelectionCleared += (_, _) => cleared++;

        await output.SetSortCommand.ExecuteAsync(SortBy.Name);

        // ClearSelection 을 부르지 않는다 — 부르면 이 수가 오른다.
        Assert.Equal(0, cleared);

        // 목록을 다시 읽었으므로 항목 객체는 새것이다. 경로로 다시 잡혔는지를 본다.
        Assert.Equal(
            [chosen.FullPath],
            output.SelectedItems.Select(i => i.FullPath));
        Assert.True(output.HasSelection);
        Assert.Same(output, f.ViewModel.SelectionOwner);
    }

    /// <summary>
    /// 통지를 손으로 불러야 한다 — <c>[NotifyPropertyChangedFor]</c> 는 <c>entry</c> 필드에
    /// 붙어 있어서 같은 <c>Entry</c> 안의 값만 바꾸면 아무 통지도 안 나간다.
    /// 빠뜨리면 목록만 바뀌고 헤더 문구가 옛 값으로 남는다.
    /// </summary>
    [Fact]
    public async Task NotificationsAccompanyTheChange()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Contains(nameof(FolderPanelViewModel.MetaSortText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.MetaText), seen);
        Assert.Equal(" · 수정 ↑", code.MetaSortText);
    }

    /// <summary>메타 세 조각을 이어 붙인 것이 문구다 — 정렬을 바꿔도 어긋나지 않는다.</summary>
    [Fact]
    public async Task MetaPartsStillJoinIntoTheMetaLine()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);

        await code.SetSortCommand.ExecuteAsync(SortBy.Type);
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);

        await code.SetSortCommand.ExecuteAsync(SortBy.Type);
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
    }

    /// <summary>
    /// 저장은 <c>pinned</c>·배치가 이미 쓰는 디바운스 통로로 간다 — 새 경로를 만들지 않았다.
    /// <c>schemaVersion</c> 도 올리지 않는다: 슬롯이 이미 있다.
    /// </summary>
    [Fact]
    public async Task TheSortSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.SetSortCommand.ExecuteAsync(SortBy.Type);
        await code.SetSortCommand.ExecuteAsync(SortBy.Type);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.Equal(SortBy.Type, entry.SortBy);
        Assert.True(entry.SortDesc);
    }

    /// <summary>아직 폴더를 안 띄운 순환 칸에는 바꿀 값이 없다 — 저장도 일어나지 않는다.</summary>
    [Fact]
    public async Task AnEmptyRotatingPanelHasNothingToSort()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        var saves = 0;
        rotating.SaveFolderEntry = () => saves++;

        await rotating.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Null(rotating.Entry);
        Assert.Null(rotating.MetaText);
        Assert.Equal(0, saves);
    }

    /// <summary>정렬은 폴더별 속성이다 — 옆 타일이 따라 바뀌지 않는다 (§3.1).</summary>
    [Fact]
    public async Task EachTileSortsOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Equal(SortBy.Modified, code.Entry!.SortBy);
        Assert.Equal(SortBy.Size, output.Entry!.SortBy);
        Assert.True(output.Entry.SortDesc);
    }

    /// <summary>
    /// <c>list</c> 뷰에서도 먹는다 — 두 <c>ListView</c> 가 같은 <c>DisplayItems</c> 를 보고
    /// 정렬은 열거 때 확정되므로 뷰 방식과 무관하다. (`list` 뷰에는 컬럼 머리글이 아예 없어서
    /// 머리글 클릭만으로는 이 칸을 못 덮는다 — 그래서 진입점이 헤더 글자다.)
    /// </summary>
    [Fact]
    public async Task TheListViewTileSortsToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        // "문서"는 list 뷰로 등록돼 있다. 순환 칸에 띄운다.
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        Assert.True(panel.IsListView);
        Assert.Equal(SortBy.Name, panel.Entry!.SortBy);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Equal(SortBy.Modified, panel.Entry.SortBy);
        Assert.False(panel.Entry.SortDesc);
        Assert.True(panel.IsListView);
        Assert.Equal(" · 수정 ↑", panel.MetaSortText);
    }

    /// <summary>저장 통로를 가로채 몇 번 불렸는지 센다 (원래 저장도 그대로 흘려보낸다).</summary>
    private static Func<int> Count(FolderPanelViewModel panel)
    {
        var saves = 0;
        var save = panel.SaveFolderEntry!;
        panel.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        return () => saves;
    }
}
