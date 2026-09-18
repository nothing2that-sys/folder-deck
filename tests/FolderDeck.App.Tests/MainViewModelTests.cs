using FolderDeck.Core.Enumeration;
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

    /// <summary>
    /// 패널에 뜬 채로 폴더가 다시 닿게 되면 F5 없이도 회색이 풀려야 한다 — 재조회 자체가
    /// 그 계기다(검토 회차 발견 · <c>UpdateRowStates</c> 편도 래치 수정의 회귀 시험. 코덱스
    /// 적대적 검증에서 이 수정에 자동 시험이 없다는 지적을 받았다).
    /// <see cref="MainWindowFixture.UnreachablePath"/>는 존재하지 않는 호스트라 이 시나리오에
    /// 못 쓴다 — 대신 로컬에 없는 경로를 만들었다가 실제로 만들어 "복구"를 흉내낸다.
    /// </summary>
    [Fact]
    public async Task ARecoveredFolderClearsInaccessibleOnItsOwnRefresh()
    {
        using var f = new MainWindowFixture();
        f.Store.Flush();
        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        var recoveringPath = Path.Combine(Path.GetDirectoryName(f.CodePath)!, "recovers");
        reopened.Folders.Add(new FolderEntry
        {
            Path = recoveringPath, DisplayName = "복구됨", Description = "시험용",
        });

        var viewModel = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt,
            f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        await viewModel.InitializeAsync();

        var row = viewModel.Rows.Single(r => r.DisplayName == "복구됨");
        Assert.True(row.IsInaccessible);

        await viewModel.ShowFolderAsync(row);
        Assert.True(viewModel.RotatingPanel!.HasFailure);

        // 실제로 만들어서 이제는 닿는다 — 재조회가 이 사실을 알아채야 한다.
        Directory.CreateDirectory(recoveringPath);
        await viewModel.RotatingPanel!.RefreshAsync();

        Assert.False(viewModel.RotatingPanel!.HasFailure);
        Assert.False(row.IsInaccessible);
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

    // ── 목록 읽기 완료 신호 (§8 · D37) ───────────────────────────────────────
    //
    // 감시자는 이 신호로 자기가 죽었는지 다시 본다. 신호가 안 나면 끊긴 네트워크에서
    // 죽은 감시자를 되살릴 트리거가 없다 — 그래서 「난다」가 아니라 「정확히 한 번
    // 난다」를 문다. 감시자 쪽은 Application·Dispatcher 를 요구해 시험에 안 매달린다.

    /// <summary>읽기가 끝나면 신호가 정확히 한 번 난다.</summary>
    [Fact]
    public async Task LoadCompletedFiresOncePerRead()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var panel = f.Rotating;
        var fired = 0;
        panel.LoadCompleted += (_, _) => fired++;

        await panel.RefreshAsync();

        Assert.Equal(1, fired);
        Assert.False(panel.HasFailure);
        Assert.Single(panel.Items);
    }

    /// <summary>
    /// <b>실패해도</b> 난다. 여기서 안 내면 「접근 불가」 배너의 "다시 시도" 로는 죽은
    /// 감시자를 되살릴 길이 없다 — 네트워크가 돌아온 뒤 제일 먼저 누르는 버튼이 그것이다.
    /// </summary>
    [Fact]
    public async Task LoadCompletedFiresOnceEvenWhenTheReadFails()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        var panel = f.Rotating;
        Assert.True(panel.HasFailure);

        var fired = 0;
        panel.LoadCompleted += (_, _) => fired++;

        // 배너의 "다시 시도" 가 부르는 그 커맨드.
        await panel.RefreshAsync();

        Assert.Equal(1, fired);
        Assert.True(panel.HasFailure);
        Assert.Equal(MainWindowFixture.UnreachablePath, panel.CurrentPath);
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

    // ── 인스턴스 관리 (§6.2 · Phase 4 Stage 1) ────────────────────────────────

    /// <summary>자동 열기가 켜져 있어도 다른 작업 관리로 갈 길이 있어야 한다 (§5).</summary>
    [Fact]
    public void OpenLauncherStartsTheLauncher()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.OpenLauncherCommand.Execute(null);

        Assert.Equal(1, f.SelfLauncher.LauncherCount);
        Assert.False(f.ViewModel.MessageIsWarning);
    }

    [Fact]
    public void FailingToStartTheLauncherIsSurfacedAndStays()
    {
        using var f = new MainWindowFixture();
        f.SelfLauncher.Error = "실행 파일 경로를 알 수 없다";

        f.ViewModel.OpenLauncherCommand.Execute(null);

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("실행 파일 경로를 알 수 없다", f.ViewModel.Message);
    }

    /// <summary>
    /// 좀비 때문에 같은 워크스페이스가 두 창에 떴다 — 그 말이 문구에 있고, 경고는 사라지지
    /// 않는다 (§3.2-3 · §5.1 "실패·경고").
    /// </summary>
    [Fact]
    public void ADuplicateWindowWarningStaysAndSaysWhy()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ReportDuplicateWindow();

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("두 창", f.ViewModel.Message);

        // 정보성 안내를 지우는 타이머가 돌아도 남아 있어야 한다.
        f.ViewModel.ExpireTransientMessage();
        Assert.True(f.ViewModel.HasMessage);
    }

    [Fact]
    public void AWindowThatCannotReceiveSignalsSaysSoAndStays()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ReportNoSignal("모든 파이프 인스턴스가 사용 중입니다.");

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("모든 파이프 인스턴스가 사용 중입니다.", f.ViewModel.Message);
    }

    // ── 플래그는 Message 알림보다 먼저 선다 (D30) ───────────────────
    //
    // 뷰(MainWindow)는 PropertyChanged("Message") 를 받은 그 자리에서 MessageIsTransient 를
    // 읽어 수명 타이머를 세울지 정한다. 그래서 "끝난 뒤의 값"이 맞는 것만으로는 부족하고,
    // 알림이 나는 그 순간에 이미 새 메시지의 값이어야 한다. 아래 셋은 뷰가 하는 판단을
    // 같은 시점에서 재현한다 — 타이머를 기다리지 않으므로 시간과 무관하다.

    /// <summary>
    /// 뷰가 하는 일을 그대로 흉내낸다 — <c>PropertyChanged("Message")</c> 가 나는 그 순간의
    /// 두 플래그를 붙잡아 둔다. 끝난 뒤의 값이 아니라 <b>그 시점</b>의 값이라야 뜻이 있다.
    /// </summary>
    private static List<(bool Warning, bool Transient)> FlagsWhenMessageFires(MainViewModel vm)
    {
        var seen = new List<(bool Warning, bool Transient)>();

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Message))
            {
                seen.Add((vm.MessageIsWarning, vm.MessageIsTransient));
            }
        };

        return seen;
    }

    /// <summary>
    /// 앱을 켜고 나서 첫 transient — 알림 시점에 이미 <c>true</c> 여야 한다. 예전에는
    /// <c>Message</c> 를 먼저 써서 뷰가 기본값 <c>false</c> 를 읽었고, 타이머가 서지 않아
    /// 거절이 영영 남았다 (D30).
    /// </summary>
    [Fact]
    public void TheFirstTransientIsAlreadyTransientWhenTheMessageNotificationFires()
    {
        using var f = new MainWindowFixture();

        var seen = FlagsWhenMessageFires(f.ViewModel);

        f.ViewModel.ReportTileOverlap();

        var (warning, transient) = Assert.Single(seen);
        Assert.True(transient);
        Assert.True(warning);
    }

    /// <summary>낡은 <c>true</c> 가 다음 메시지로 새지 않는다 — 남는 경고는 알림 시점에 <c>false</c>.</summary>
    [Fact]
    public void AStayingWarningDoesNotInheritTheTransientFlagOfTheOneBefore()
    {
        using var f = new MainWindowFixture();
        f.ViewModel.ReportTileOverlap();

        var seen = FlagsWhenMessageFires(f.ViewModel);

        f.ViewModel.ReportDuplicateWindow();

        var (warning, transient) = Assert.Single(seen);
        Assert.False(transient);
        Assert.True(warning);
    }

    /// <summary>지우는 것도 같은 계약이다 — 알림 시점에 두 플래그가 이미 내려가 있다.</summary>
    [Fact]
    public void ClearingLowersBothFlagsBeforeTheMessageNotification()
    {
        using var f = new MainWindowFixture();
        f.ViewModel.ReportTileOverlap();

        var seen = FlagsWhenMessageFires(f.ViewModel);

        f.ViewModel.ExpireTransientMessage();

        var (warning, transient) = Assert.Single(seen);
        Assert.False(transient);
        Assert.False(warning);
    }
    // ── 재조회는 등급을 가린다 (D31) ─────────────────────────────────────────
    //
    // RefreshAllAsync 는 F5 만 부르는 것이 아니다 — 창 활성화(MainWindow.OnActivated)도
    // 같은 경로로 들어온다. 그래서 무조건 지우면 다른 프로그램에 갔다 돌아오기만 해도
    // 남아야 할 실패 경고가 사라진다. §5.1 이 실패·경고를 「남는다」로 못박았으므로
    // 지우는 것은 등급을 가려야 한다.

    /// <summary>남아야 할 경고는 재조회를 넘긴다 — alt-tab 한 번에 실패를 잃지 않는다 (D31).</summary>
    [Fact]
    public async Task RefreshKeepsAWarningThatIsMeantToStay()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.ReportDuplicateWindow();
        Assert.False(f.ViewModel.MessageIsTransient);

        await f.ViewModel.RefreshAllAsync();

        Assert.True(f.ViewModel.HasMessage);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("두 창", f.ViewModel.Message);
    }

    /// <summary>사라질 것은 재조회가 그대로 걷어 간다 — 등급을 가릴 뿐 안 지우는 것이 아니다.</summary>
    [Fact]
    public async Task RefreshStillClearsATransientRefusal()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.ReportTileOverlap();
        Assert.True(f.ViewModel.MessageIsTransient);

        await f.ViewModel.RefreshAllAsync();

        Assert.False(f.ViewModel.HasMessage);
    }
}
