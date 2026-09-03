using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 상세 뷰의 `크기`·`수정` 열을 끄고 켠다 (Phase 15 · D79 · design.md §3.2). 진입점(컬럼 머리글
/// 우클릭 메뉴)은 D79-c 가 붙인다 — 여기서는 커맨드가 정확한 상태를 만드는지만 문다.
/// </summary>
/// <remarks>
/// <b>폭 저장이 아니라 표시 토글이다</b> — <see cref="FolderPanelViewModel.ToggleFoldersFirstCommand"/>
/// 와 달리 <b>목록을 다시 읽지 않는다</b>: 컬럼 표시는 열거 결과를 바꾸지 않는다(정렬·「폴더
/// 먼저」와 다른 점, D79-b §6-4). 검색 타일은 <c>Entry</c> 가 없어 저장되지 않는다(§7.1 v7).
/// </remarks>
public sealed class ColumnVisibilityTests
{
    /// <summary>패널만 떼어 만든다 — <see cref="CurrentFolderMenuSearchGuardTests"/> 의 <c>BareSearchPanel</c> 과 같은 결.</summary>
    private static FolderPanelViewModel BareSearchPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: null);

    /// <summary>패널만 떼어 만든다 — 검색 타일이 아닌 쪽.</summary>
    private static FolderPanelViewModel BareFolderPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: false,
        reportError: null,
        reportRejection: null);

    [Fact]
    public async Task TurningOffShowSizeFlipsTheFolderEntryAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.Entry!.ShowSize);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        code.ToggleShowSizeCommand.Execute(null);

        Assert.False(code.Entry!.ShowSize);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task TurningOffShowModifiedFlipsTheFolderEntryAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.Entry!.ShowModified);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        code.ToggleShowModifiedCommand.Execute(null);

        Assert.False(code.Entry!.ShowModified);
        Assert.Equal(1, saves);
    }

    /// <summary>다시 누르면 되돌아온다.</summary>
    [Fact]
    public async Task TogglingBackRestoresShowSize()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        code.ToggleShowSizeCommand.Execute(null);
        code.ToggleShowSizeCommand.Execute(null);

        Assert.True(code.Entry!.ShowSize);
    }

    /// <summary>
    /// 컬럼 표시는 열거 결과를 바꾸지 않는다 — 목록을 다시 읽지 않는다(D79-b §6-4 판단).
    /// <c>Items</c> 는 열거가 끝날 때만 새 배열로 갈린다 — 같은 참조로 남으면 다시 읽지 않은
    /// 것이다.
    /// </summary>
    [Fact]
    public async Task TogglingShowSizeDoesNotReenumerateTheListing()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var itemsBeforeToggle = code.Items;

        code.ToggleShowSizeCommand.Execute(null);

        Assert.Same(itemsBeforeToggle, code.Items);
    }

    /// <summary>값은 폴더마다다 — 순환 칸을 다른 폴더로 갈아타면 그 폴더의 값으로 갈아탄다.</summary>
    [Fact]
    public async Task TheChoiceTravelsWithTheFolderInARotatingSlot()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        Assert.True(panel.Entry!.ShowSize);

        panel.ToggleShowSizeCommand.Execute(null);
        Assert.False(panel.Entry!.ShowSize);

        // 다른 폴더로 갈아탄다 — 그 폴더는 기본값(보인다) 그대로다.
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.True(panel.Entry!.ShowSize);

        // 되돌아가면 꺼둔 값이 그대로 남아 있다.
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.False(panel.Entry!.ShowSize);
    }

    /// <summary>타일마다 따로다 — 옆 칸이 따라 바뀌지 않는다.</summary>
    [Fact]
    public async Task EachTileTogglesOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.Panels[0].ToggleShowSizeCommand.Execute(null);

        Assert.False(f.ViewModel.Panels[0].Entry!.ShowSize);
        Assert.True(f.ViewModel.Panels[1].Entry!.ShowSize);
    }

    /// <summary>저장되므로 다시 열어도 남는다.</summary>
    [Fact]
    public async Task TheChoiceSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.ToggleShowSizeCommand.Execute(null);
        code.ToggleShowModifiedCommand.Execute(null);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.False(entry.ShowSize);
        Assert.False(entry.ShowModified);

        // 옆 슬롯은 건드리지 않았다.
        Assert.Equal(SortBy.Name, entry.SortBy);
        Assert.True(entry.FoldersFirst);
    }

    /// <summary>아직 폴더를 안 띄운 순환 칸에는 바꿀 것이 없다.</summary>
    [Fact]
    public void AnEmptyPanelHasNothingToToggle()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        rotating.ToggleShowSizeCommand.Execute(null);
        rotating.ToggleShowModifiedCommand.Execute(null);

        Assert.True(rotating.IsEmpty);
    }

    /// <summary>`이름` 열에는 커맨드 자체가 없다 — 끌 수 없다(design.md §3.2 · D79b-7).</summary>
    [Fact]
    public void ThereIsNoCommandToHideTheNameColumn()
    {
        var memberNames = typeof(FolderPanelViewModel)
            .GetMembers()
            .Select(m => m.Name);

        Assert.DoesNotContain("ToggleShowNameCommand", memberNames);
    }

    /// <summary>
    /// 검색 타일에서 토글해도 <c>Entry</c> 도 저장도 건드리지 않는다 — 검색 타일엔 <c>Entry</c>
    /// 자체가 없고(§7.1 v7), 저장할 자리도 없다(D78 B-0 §5).
    /// </summary>
    [Fact]
    public void TogglingShowSizeOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowSizeCommand.Execute(null);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void TogglingShowModifiedOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowModifiedCommand.Execute(null);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);
    }

    /// <summary>
    /// `위치` 열은 검색 뷰에만 있다(D78 B-0 §4) — 폴더 타일에는 이 열 자체가 없어 토글이
    /// 아무 일도 하지 않는다(D79-b §6-3).
    /// </summary>
    [Fact]
    public void TogglingShowPositionOnAFolderTileIsANoOp()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.Equal(0, saves);
    }

    /// <summary>
    /// 검색 타일에서 토글하면 <see cref="FolderPanelViewModel.CurrentShowSize"/> 자체가 뒤집힌다
    /// — D79-b-fix 가 메운 구멍(D79-b 보고문 §5 R2). 다시 누르면 되돌아온다.
    /// </summary>
    [Fact]
    public void TogglingShowSizeOnASearchTileFlipsCurrentShowSize()
    {
        var panel = BareSearchPanel();
        Assert.True(panel.CurrentShowSize);

        panel.ToggleShowSizeCommand.Execute(null);
        Assert.False(panel.CurrentShowSize);

        panel.ToggleShowSizeCommand.Execute(null);
        Assert.True(panel.CurrentShowSize);
    }

    /// <summary>
    /// 폴더 타일에서 <see cref="FolderPanelViewModel.CurrentShowSize"/>/
    /// <see cref="FolderPanelViewModel.CurrentShowModified"/> 는 <c>Entry</c> 를 그대로 따른다 —
    /// <c>Entry</c> 쪽 값을 바꾸면(꼭 커맨드를 거치지 않아도) 프로퍼티도 따라 바뀐다.
    /// </summary>
    [Fact]
    public void CurrentShowSizeAndCurrentShowModifiedFollowTheFolderEntry()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };

        Assert.True(panel.CurrentShowSize);
        Assert.True(panel.CurrentShowModified);

        panel.Entry.ShowSize = false;
        panel.Entry.ShowModified = false;

        Assert.False(panel.CurrentShowSize);
        Assert.False(panel.CurrentShowModified);
    }

    /// <summary>
    /// 검색 타일에서 토글하면 <see cref="FolderPanelViewModel.CurrentShowPosition"/> 이 뒤집힌다
    /// — D79-b 보고문 §9 가 「겉에서 볼 길이 없다」고 적었던 자리다.
    /// </summary>
    [Fact]
    public void TogglingShowPositionOnASearchTileFlipsCurrentShowPosition()
    {
        var panel = BareSearchPanel();
        Assert.True(panel.CurrentShowPosition);

        panel.ToggleShowPositionCommand.Execute(null);
        Assert.False(panel.CurrentShowPosition);
    }

    private static async Task WaitForSearchToSettle(FolderPanelViewModel panel)
    {
        for (var i = 0; i < 200 && panel.IsSearching; i++)
        {
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// 재귀 검색 중인 <b>폴더 타일</b>에서 `위치` 토글이 실제로 값을 바꾸는지(Phase 15 ·
    /// D80-b) — 가드가 <c>IsSearchTile</c> 하나만 봐서 재귀 검색(검색 타일이 아니다 — 폴더
    /// 타일이 검색 중인 것)에서는 조기 반환에 걸렸었다. `크기`/`수정` 은 <c>else</c> 가지가
    /// <see cref="FolderPanelViewModel.Entry"/> 를 보므로 이 결함과 무관했다(D80-b §2).
    /// </summary>
    [Fact]
    public async Task TogglingShowPositionDuringARecursiveSearchFlipsCurrentShowPosition()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);
        Assert.True(panel.NeedsLocationColumn);
        Assert.True(panel.CurrentShowPosition);

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.False(panel.CurrentShowPosition);
    }

    /// <summary>
    /// 값만 바뀌고 통지가 없으면 <c>ApplyColumnVisibility</c> 가 안 깨어나 화면은 그대로다 —
    /// 값 전이와 통지를 갈라서 문다(D80-b §2).
    /// </summary>
    [Fact]
    public async Task TogglingShowPositionDuringARecursiveSearchNotifiesCurrentShowPosition()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SearchText = "Recipe";
        panel.IncludeSubfolders = true;
        await WaitForSearchToSettle(panel);

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.Contains(nameof(FolderPanelViewModel.CurrentShowPosition), seen);
    }

    /// <summary>
    /// 재귀 검색도 아니고 검색 타일도 아니면 여전히 아무 일도 안 한다(B-2) — 가드를
    /// 없애지 않았다는 것을 못박는다. 값도 안 바뀌고 통지도 없다.
    /// </summary>
    [Fact]
    public async Task TogglingShowPositionOnAnOrdinaryFolderTileStillDoesNothing()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        Assert.False(panel.NeedsLocationColumn);
        var before = panel.CurrentShowPosition;

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.ToggleShowPositionCommand.Execute(null);

        Assert.Equal(before, panel.CurrentShowPosition);
        Assert.DoesNotContain(nameof(FolderPanelViewModel.CurrentShowPosition), seen);
    }
}
