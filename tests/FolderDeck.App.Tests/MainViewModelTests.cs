using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>왼쪽 리스트 · 배치 유도 · 순환 칸 제어 (§3.2 · §4.1).</summary>
public sealed class MainViewModelTests
{
    [Fact]
    public void LeftListShowsEveryFolderWithPinAndDescription()
    {
        using var f = new MainWindowFixture();

        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Equal(
            ["code", "산출물", "작업", "문서", "설비 로그"],
            f.ViewModel.Rows.Select(r => r.DisplayName));

        Assert.True(f.Row("code").Pinned);
        Assert.True(f.Row("산출물").Pinned);
        Assert.False(f.Row("문서").Pinned);

        Assert.Equal("구현 코드", f.Row("code").Description);
        Assert.True(f.Row("code").HasDescription);
    }

    /// <summary>tiles[] 가 없는 워크스페이스는 pinned 에서 한 번 유도된다 (v1/v2 → v3).</summary>
    [Fact]
    public void PanelsAreDerivedFromPinnedPlusOneRotating()
    {
        using var f = new MainWindowFixture();

        // 상시 2개 + 순환 1개
        Assert.Equal(3, f.ViewModel.Panels.Count);
        Assert.Equal(2, f.ViewModel.Panels.Count(p => !p.IsRotating));
        Assert.Single(f.ViewModel.Panels, p => p.IsRotating);

        // 순환 패널은 마지막 자리.
        Assert.True(f.ViewModel.Panels[^1].IsRotating);
        Assert.Same(f.Rotating, f.ViewModel.Panels[^1]);

        // 상시 패널은 앵커가 못박혀 있고, 순환 패널은 비어서 시작한다.
        Assert.Equal([f.CodePath, f.OutputPath], f.ViewModel.Panels.Where(p => !p.IsRotating).Select(p => p.Entry!.Path));
        Assert.True(f.Rotating.IsEmpty);
    }

    /// <summary>Phase 2 부터 배치는 <c>tiles[]</c> 가 정답이다 — 유도는 한 번뿐.</summary>
    [Fact]
    public void TilesArrayIsFilledInAndBecomesTheSourceOfTruth()
    {
        using var f = new MainWindowFixture();

        Assert.NotNull(f.Workspace.Tiles);
        Assert.Equal(3, f.Workspace.Tiles!.Count);
        Assert.Equal(4, f.Workspace.Grid!.Cols);

        // 뷰모델의 타일은 저장 대상 TileSpec 을 그대로 들고 있다.
        Assert.Equal(f.Workspace.Tiles, f.ViewModel.Tiles.Select(t => t.Spec));

        // 상시 2 + 순환 1 을 4x4 에 균등 분할 → 2x2 세 장.
        Assert.All(f.ViewModel.Tiles, t => Assert.Equal((2, 2), (t.SpanX, t.SpanY)));
        Assert.Equal([(0, 0), (2, 0), (0, 2)], f.ViewModel.Tiles.Select(t => (t.CellX, t.CellY)));

        // 남은 칸(2,2) 자리 2x2 = 네 칸이 빈 칸으로 잡힌다.
        Assert.Equal(4, f.ViewModel.Cells.Count(c => c.IsFree));
    }

    [Fact]
    public async Task InitializeLoadsPinnedPanelsFromTheirAnchors()
    {
        using var f = new MainWindowFixture();

        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(f.CodePath, code.CurrentPath);
        Assert.False(code.IsAwayFromAnchor);
        Assert.False(code.HasFailure);

        // 폴더 먼저, 그 다음 파일.
        Assert.Equal(["Recipe", "Views", "Main.cs"], code.Items.Select(i => i.Name));
        Assert.True(code.Items[0].IsDirectory);
    }

    [Fact]
    public async Task ClickingARowShowsThatFolderInTheRotatingPanel()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var rotating = f.Rotating;
        Assert.Equal(f.DocsPath, rotating.CurrentPath);
        Assert.Equal("spec.md", Assert.Single(rotating.Items).Name);
        Assert.True(f.Row("문서").IsShowingInRotating);
    }

    /// <summary>
    /// 상시 칸에 이미 못박힌 폴더는 순환 칸을 뺏지 않는다 — 화면에 이미 있는 것을 또 띄우면
    /// 순환 칸 한 자리가 낭비된다. 대신 자기 상시 칸이 앵커로 돌아온다.
    /// </summary>
    [Fact]
    public async Task ClickingAPinnedRowResetsItsOwnTileAndLeavesTheRotatingCellAlone()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var pinned = f.ViewModel.Panels[0];
        await pinned.OpenAsync(pinned.Items.Single(i => i.Name == "Recipe"));
        Assert.True(pinned.IsAwayFromAnchor);

        await f.ViewModel.ShowFolderAsync(f.Row("code"));

        // 상시 칸은 집으로 돌아오고, 순환 칸은 보던 것을 그대로 들고 있다.
        Assert.Equal(f.CodePath, pinned.CurrentPath);
        Assert.False(pinned.IsAwayFromAnchor);
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
        Assert.False(f.Row("code").IsShowingInRotating);

        // 이미 앵커에 있는데 또 누르면 왜 아무 일도 안 일어나는지 말해 준다.
        await f.ViewModel.ShowFolderAsync(f.Row("code"));
        Assert.Contains("상시 칸에 이미 떠 있다", f.ViewModel.Message);
    }

    [Fact]
    public async Task ClickingAnotherRowReplacesWhatTheRotatingPanelShows()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        Assert.Equal(f.WorkPath, f.Rotating.CurrentPath);
        Assert.False(f.Row("문서").IsShowingInRotating);
        Assert.True(f.Row("작업").IsShowingInRotating);
    }

    /// <summary>상태 바 = 순환 칸의 전체 경로 (§3.2).</summary>
    [Fact]
    public async Task StatusBarFollowsTheRotatingPanel()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.DoesNotContain(@"\", f.ViewModel.StatusPath);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.Equal(f.WorkPath, f.ViewModel.StatusPath);

        var recipe = f.Rotating.Items.Single(i => i.Name == "Recipe");
        await f.Rotating.OpenAsync(recipe);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.ViewModel.StatusPath);
    }

    /// <summary>viewMode 는 저장값대로 다르게 그려진다 — UI 에서 바꾸는 건 Stage 2 밖.</summary>
    [Fact]
    public async Task ViewModeAndSortComeFromTheSavedEntry()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.IsDetailsView);
        Assert.False(code.IsListView);
        Assert.Contains("details", code.MetaText);
        Assert.Contains("이름 ↑", code.MetaText);

        // 산출물 = 크기 내림차순 저장값.
        var output = f.ViewModel.Panels[1];
        Assert.Contains("크기 ↓", output.MetaText);
        Assert.Equal(["b.dll", "a.dll"], output.Items.Select(i => i.Name));

        // 문서 = list 뷰.
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.True(f.Rotating.IsListView);
        Assert.False(f.Rotating.IsDetailsView);
        Assert.Contains("list", f.Rotating.MetaText);
    }

    // ── 접근 불가 (§5) ───────────────────────────────────────────────────────

    [Fact]
    public async Task UnreachableFolderRendersAsInaccessibleAndStaysInTheList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.True(f.Row("설비 로그").IsInaccessible);

        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        var rotating = f.Rotating;
        Assert.True(rotating.HasFailure);
        Assert.Empty(rotating.Items);
        Assert.Contains("접근 불가", rotating.FailureText);

        // 목록에서 지우지 않는다.
        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Contains(MainWindowFixture.UnreachablePath, f.ViewModel.Rows.Select(r => r.PathText));
        Assert.True(f.Row("설비 로그").IsInaccessible);
    }

    /// <summary>"다시 시도" = 같은 열거를 다시 돌린다. 복구되면 그때 내용이 뜬다.</summary>
    [Fact]
    public async Task RetryReEnumeratesAnInaccessibleFolder()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));
        Assert.True(f.Rotating.HasFailure);

        await f.Rotating.RefreshAsync();

        // 아직 닿지 않으니 실패 상태 유지 — 경로도 그대로다.
        Assert.True(f.Rotating.HasFailure);
        Assert.Equal(MainWindowFixture.UnreachablePath, f.Rotating.CurrentPath);
    }

    [Fact]
    public async Task AReachableFolderIsNotMarkedInaccessible()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.False(f.Row("code").IsInaccessible);
        Assert.False(f.Row("문서").IsInaccessible);
    }

    // ── 갱신 (§8) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshPicksUpAFileDroppedIntoTheFolder()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(f.Rotating.Items);

        File.WriteAllText(Path.Combine(f.DocsPath, "새파일.txt"), "새 내용");

        // F5 / 창 활성화가 부르는 것과 같은 경로.
        await f.ViewModel.RefreshAllAsync();

        Assert.Equal(2, f.Rotating.Items.Count);
        Assert.Contains("새파일.txt", f.Rotating.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task RefreshAlsoUpdatesPinnedPanels()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var before = f.ViewModel.Panels[0].Items.Count;

        File.WriteAllText(Path.Combine(f.CodePath, "Added.cs"), "x");
        await f.ViewModel.RefreshAllAsync();

        Assert.Equal(before + 1, f.ViewModel.Panels[0].Items.Count);
    }

    [Fact]
    public async Task RefreshKeepsTheSubfolderYouNavigatedInto()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var recipe = f.Rotating.Items.Single(i => i.Name == "Recipe");
        await f.Rotating.OpenAsync(recipe);

        await f.ViewModel.RefreshAllAsync();

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.Rotating.CurrentPath);
    }

    // ── 파일 열기 (§4.5) ─────────────────────────────────────────────────────

    [Fact]
    public async Task DoubleClickingAFileHandsItToTheShell()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var file = f.Rotating.Items.Single(i => i.Name == "spec.md");
        await f.Rotating.OpenAsync(file);

        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), Assert.Single(f.Shell.Opened));

        // 파일을 열어도 패널은 그 자리에 머문다.
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
    }

    [Fact]
    public async Task DoubleClickingAFolderNavigatesInsteadOfShellingOut()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        var recipe = f.Rotating.Items.Single(i => i.Name == "Recipe");
        await f.Rotating.OpenAsync(recipe);

        Assert.Empty(f.Shell.Opened);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.Rotating.CurrentPath);
    }

    [Fact]
    public async Task ShellFailureIsSurfacedNotSwallowed()
    {
        using var f = new MainWindowFixture();
        f.Shell.Error = "연결된 프로그램이 없다";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        await f.Rotating.OpenAsync(
            f.Rotating.Items.Single(i => i.Name == "spec.md"));

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("연결된 프로그램이 없다", f.ViewModel.Message);
    }
}
