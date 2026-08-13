using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>복사 대상함 + 다중 선택 복사/이동 (§3.2 · §4.2 · §4.3 · §4.8 · Stage 2).</summary>
public sealed class CopyTrayTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    // ── 대상함 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task FoldersGoIntoTheTrayFromTheLeftListAndPersist()
    {
        using var f = await ReadyAsync();

        Assert.True(f.ViewModel.TrayIsEmpty);

        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        Assert.Equal(["산출물", "문서"], f.ViewModel.Tray.Select(t => t.DisplayName));
        Assert.All(f.ViewModel.Tray, t => Assert.True(t.IsChecked));

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(
            [f.Row("산출물").Entry.Id, f.Row("문서").Entry.Id],
            reloaded.CopyTray!);
    }

    [Fact]
    public async Task TheSameFolderIsNotAddedTwice()
    {
        using var f = await ReadyAsync();
        f.ViewModel.AddToTray(f.Row("산출물"));

        f.ViewModel.AddToTray(f.Row("산출물"));

        Assert.Single(f.ViewModel.Tray);
        Assert.Contains("대상함에 이미 있다", f.ViewModel.Message);
    }

    [Fact]
    public async Task RemovingFromTheTrayLeavesTheFolderRegistered()
    {
        using var f = await ReadyAsync();
        f.ViewModel.AddToTray(f.Row("산출물"));

        f.ViewModel.Tray[0].RemoveCommand.Execute(null);

        Assert.True(f.ViewModel.TrayIsEmpty);
        Assert.Contains(f.ViewModel.Rows, r => r.DisplayName == "산출물");

        // 비면 슬롯 자체를 지운다 — 빈 배열을 파일에 남기지 않는다.
        f.Store.Flush();
        Assert.Null(f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow().CopyTray);
    }

    [Fact]
    public async Task TheTrayComesBackAfterARestart()
    {
        using var f = await ReadyAsync();
        f.ViewModel.AddToTray(f.Row("문서"));
        f.Store.Flush();

        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt, f.MacroEditor,
            f.FolderEditor, f.SelfLauncher);

        Assert.Equal("문서", Assert.Single(restarted.Tray).DisplayName);
        Assert.True(restarted.Tray[0].IsChecked);
    }

    // ── 선택은 타일 하나에만 (§4.8) ──────────────────────────────────────────

    [Fact]
    public async Task SelectingInOneTileClearsTheOther()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        Select(code, "Main.cs");
        Assert.Same(code, f.ViewModel.SelectionOwner);
        Assert.True(f.ViewModel.HasSelection);

        Select(output, "a.dll");

        Assert.Same(output, f.ViewModel.SelectionOwner);
        Assert.False(code.HasSelection);
        Assert.True(output.HasSelection);
    }

    [Fact]
    public async Task LeavingAFolderDropsItsSelection()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        Assert.True(f.ViewModel.HasSelection);

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.False(f.Rotating.HasSelection);
        Assert.Null(f.ViewModel.SelectionOwner);
    }

    /// <summary>
    /// 목록을 다시 읽어도 고른 것은 살아 있어야 한다 (§4.8).
    /// </summary>
    /// <remarks>
    /// 창을 잠깐 떠났다 돌아오기만 해도(§8 활성화 재조회) 선택이 사라지면, 매크로를 만들고
    /// 바로 실행할 때 "대상이 없다"가 뜬다 — 실제로 그랬다. 경로로 다시 잡아 준다.
    /// </remarks>
    [Fact]
    public async Task ARefreshKeepsTheSelection()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");

        await f.ViewModel.RefreshAllAsync();

        Assert.True(f.ViewModel.HasSelection);
        Assert.Equal(["Main.cs", "Recipe"], code.SelectedItems.Select(i => i.Name).Order());

        // 옛 객체가 아니라 <b>새 목록의 객체</b>를 잡고 있어야 한다.
        Assert.All(code.SelectedItems, i => Assert.Contains(i, code.Items));
    }

    /// <summary>옮겨서 사라진 파일은 선택에서 자연히 빠진다 — 없는 것을 고른 채로 두지 않는다.</summary>
    [Fact]
    public async Task AFileThatVanishedDropsOutOfTheSelection()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.Prompt.Answer = true;

        await f.ViewModel.MoveSelectionAsync();

        Assert.False(f.Rotating.HasSelection);
        Assert.False(f.ViewModel.HasSelection);
    }

    // ── 요약과 버튼 상태 (§4.7 미리보기) ─────────────────────────────────────

    [Fact]
    public async Task TheSummaryTellsYouWhatIsMissing()
    {
        using var f = await ReadyAsync();

        Assert.Contains("파일을 고르면", f.ViewModel.OperationSummary);
        Assert.False(f.ViewModel.CanRunOperation);

        Select(f.ViewModel.Panels[0], "Main.cs", "Recipe");
        Assert.Contains("목적지를 체크하라", f.ViewModel.OperationSummary);
        Assert.False(f.ViewModel.CanRunOperation);

        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        // 파일 2개 × 폴더 2개 = 4건.
        Assert.StartsWith("파일 2개 → 폴더 2개 = 4건", f.ViewModel.OperationSummary);
        Assert.True(f.ViewModel.CanRunOperation);
    }

    [Fact]
    public async Task UncheckingADestinationTakesItOutOfTheCount()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.NotifyOperationState();

        Assert.Equal("파일 1개 → 폴더 1개 = 1건", f.ViewModel.OperationSummary);
    }

    // ── 실행 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CopyGoesToEveryCheckedDestination()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));   // 복사라 원본은 남는다
        Assert.Contains("성공 2", f.ViewModel.Message);
    }

    /// <summary>이동은 원본이 사라진다 — 확인을 거친다 (§4.7).</summary>
    [Fact]
    public async Task MoveAsksFirstAndCanBeCalledOff()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        f.Prompt.Answer = false;
        await f.ViewModel.MoveSelectionAsync();

        Assert.Contains("원본은 사라진다", Assert.Single(f.Prompt.Messages));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("취소", f.ViewModel.Message);
    }

    [Fact]
    public async Task ConfirmedMoveRemovesTheOriginal()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        f.Prompt.Answer = true;
        await f.ViewModel.MoveSelectionAsync();

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    /// <summary>덮어쓰기는 차단하지 않되 일어났음을 알린다 (§4.3).</summary>
    [Fact]
    public async Task OverwritingIsAllowedAndAnnounced()
    {
        using var f = await ReadyAsync();
        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");

        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();

        Assert.Contains("덮어씀 1", f.ViewModel.Message);
        Assert.Equal(300, new FileInfo(Path.Combine(f.DocsPath, "Main.cs")).Length);
    }

    /// <summary>하나가 실패해도 나머지는 되고, 사유가 남는다 (§5).</summary>
    [Fact]
    public async Task PartialFailureIsSummarisedAndDetailed()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.ViewModel.AddToTray(f.Row("설비 로그"));   // 닿을 수 없는 네트워크 경로

        await f.ViewModel.CopySelectionAsync();

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.Contains("건너뜀 1", f.ViewModel.Message);

        // 요약은 상태 바에 남고(사라지지 않는다), 사유는 따로 보여 준다.
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("목적지에 닿을 수 없다", Assert.Single(f.Prompt.Reports));
    }

    [Fact]
    public async Task AFolderCanBeCopiedWholeAndRecursively()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Recipe");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs")));
        Assert.Contains("성공 1", f.ViewModel.Message);
    }

    /// <summary>실행 뒤에는 목록이 실제 상태를 보여야 한다 — 방금 한 일을 눈으로 확인할 수 있게.</summary>
    [Fact]
    public async Task ListsRefreshAfterTheOperation()
    {
        using var f = await ReadyAsync();
        var docsTile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(docsTile.Panel.Items);

        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        await f.ViewModel.CopySelectionAsync();

        Assert.Equal(2, docsTile.Panel.Items.Count);
        Assert.Contains("Main.cs", docsTile.Panel.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task NothingRunsWithoutSelectionOrDestination()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.CopySelectionAsync();
        Assert.Empty(f.Prompt.Reports);

        f.ViewModel.AddToTray(f.Row("문서"));
        await f.ViewModel.CopySelectionAsync();

        Assert.Single(Directory.GetFiles(f.DocsPath));
    }

    // ── 상시 칸 등록 후에도 나머지가 정상인가 (사용자 신고) ─────────────────

    /// <summary>
    /// 빈 칸에 폴더를 넣어 상시로 만들면 왼쪽 행 글리프도 상시가 돼야 한다.
    /// (Phase 2 까지 글리프가 folders[].pinned 만 봐서 순환 모양으로 남아 있었다.)
    /// </summary>
    [Fact]
    public async Task PlacingAFolderInAPinnedCellFlipsTheLeftRowGlyph()
    {
        using var f = await ReadyAsync();
        var row = f.Row("문서");

        Assert.False(row.Pinned);
        Assert.False(row.Entry.Pinned);

        f.ViewModel.ToggleLayoutEdit();
        Assert.True(await f.ViewModel.PlaceFolderInCellAsync(row, f.ViewModel.Cells.First(c => c.IsFree)));
        f.ViewModel.ToggleLayoutEdit();

        Assert.True(row.HasPinnedTile);
        Assert.True(row.Pinned);

        // 저장 필드도 따라간다 — 런처 카드의 칩과 v1/v2 유도가 이걸 본다.
        Assert.True(row.Entry.Pinned);

        // 타일을 빼면 되돌아온다.
        f.ViewModel.ToggleLayoutEdit();
        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == row.Entry.Id));
        Assert.False(row.Pinned);
        Assert.False(row.Entry.Pinned);
    }

    /// <summary>상시 칸에 올라간 폴더도 대상함에는 넣을 수 있어야 한다 — 별개의 개념이다.</summary>
    [Fact]
    public async Task AFolderOnAPinnedTileCanStillGoIntoTheTray()
    {
        using var f = await ReadyAsync();
        var row = f.Row("문서");

        f.ViewModel.ToggleLayoutEdit();
        await f.ViewModel.PlaceFolderInCellAsync(row, f.ViewModel.Cells.First(c => c.IsFree));
        f.ViewModel.ToggleLayoutEdit();

        f.ViewModel.AddToTray(row);

        Assert.Equal("문서", Assert.Single(f.ViewModel.Tray).DisplayName);
    }

    /// <summary>처음부터 상시였던 폴더(유도 배치)도 마찬가지다.</summary>
    [Fact]
    public async Task AlreadyPinnedFoldersGoIntoTheTrayToo()
    {
        using var f = await ReadyAsync();

        f.ViewModel.AddToTray(f.Row("code"));

        Assert.Equal("code", Assert.Single(f.ViewModel.Tray).DisplayName);
    }

    // ── 이동은 목적지 하나만 (사용자 신고) ──────────────────────────────────

    /// <summary>
    /// 파일 하나가 두 곳에 있을 수는 없다. 그대로 두면 첫 목적지로 옮겨진 뒤 나머지가
    /// "원본이 없다"로 실패한다 — 시도 자체를 막는다.
    /// </summary>
    [Fact]
    public async Task MoveRefusesMoreThanOneDestination()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.MoveSelectionAsync();

        Assert.Contains("목적지 하나만", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);

        // 아무것도 건드리지 않는다 — 확인창도 뜨지 않는다.
        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    /// <summary>체크를 하나로 줄이면 그대로 된다. 복사는 여럿이어도 정상이다 (§4.2).</summary>
    [Fact]
    public async Task OneDestinationMovesAndManyDestinationsStillCopy()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        // 복사는 두 곳 모두.
        await f.ViewModel.CopySelectionAsync();
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));

        // 하나만 체크하면 이동이 된다.
        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.NotifyOperationState();
        Assert.DoesNotContain("이동은 목적지 하나만", f.ViewModel.OperationSummary);

        await f.ViewModel.MoveSelectionAsync();
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    /// <summary>두 곳이 체크돼 있으면 누르기 전에 요약이 미리 말해 준다.</summary>
    [Fact]
    public async Task TheSummaryWarnsAboutMoveBeforeYouPressIt()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        Assert.Contains("이동은 목적지 하나만", f.ViewModel.OperationSummary);
    }

    // ── 겹친 새로고침 (사용자 신고 크래시) ──────────────────────────────────

    /// <summary>
    /// 새 열거가 이전 열거의 CancellationTokenSource 를 Dispose 하면, 아직 await 에 걸려
    /// 있던 그쪽이 깨어나 자기 토큰을 읽는 순간 ObjectDisposedException 으로 죽었다.
    /// 파일 이동 뒤 결과 대화상자가 창을 재활성화하면서 새로고침이 겹칠 때 실제로 터졌다.
    /// </summary>
    [Fact]
    public async Task OverlappingRefreshesDoNotThrow()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        // 기다리지 않고 여러 번 겹쳐 던진다 — 앞의 것이 뒤의 것에 취소당하는 상황.
        var storm = Enumerable.Range(0, 8).Select(_ => f.ViewModel.RefreshAllAsync()).ToList();

        await Task.WhenAll(storm);

        // 마지막 열거는 살아남아 목록이 비어 있지 않다.
        Assert.NotEmpty(f.ViewModel.Panels[0].Items);
    }

    [Fact]
    public async Task OverlappingNavigationDoesNotThrow()
    {
        using var f = await ReadyAsync();
        var panel = f.Rotating;

        var storm = new List<Task>
        {
            f.ViewModel.ShowFolderAsync(f.Row("작업")),
            f.ViewModel.ShowFolderAsync(f.Row("문서")),
            f.ViewModel.ShowFolderAsync(f.Row("작업")),
            panel.RefreshAsync(),
        };

        await Task.WhenAll(storm);

        Assert.False(panel.IsLoading);
    }
}
