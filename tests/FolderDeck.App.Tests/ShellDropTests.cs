using System.ComponentModel;
using System.Windows;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>
/// 셸에서 끌어온 것을 받기 (§4.9 · Phase 5 Stage 1).
/// </summary>
/// <remarks>
/// 여기서 시험하는 것은 <b>판정과 실행</b>이다 — 실제 마우스 제스처와 <c>DataObject</c> 는 뷰의
/// 몫이라 흉내 내지 않는다(<see cref="DragDropTests"/> 와 같은 경계). 셸 드롭의 목적지는 타일의
/// <c>CurrentPath</c> 라 <c>FolderEntry</c> 가 없으므로, 경로로 받는 진입점
/// (<see cref="MainViewModel.DropOntoPathAsync"/>)을 그대로 부른다.
/// </remarks>
public sealed class ShellDropTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    /// <summary>셸이 주는 것은 경로 문자열뿐이다 — 폴더 여부는 디스크에 물어본다(§4.9).</summary>
    private static IReadOnlyList<OperationItem> ShellItems(params string[] paths) =>
        [.. paths.Select(p => new OperationItem(p, Directory.Exists(p)))];

    // ── ① 온 곳과 같은 폴더 — 판정 단위는 항목이다 (§4.9) ────────────────────

    /// <summary>전부 목적지에서 온 것이면 받지 않는다 — 과녁도 켜지 않는다.</summary>
    [Fact]
    public async Task EveryItemFromTheDestinationMeansNothingToAccept()
    {
        using var f = await ReadyAsync();

        var items = ShellItems(
            Path.Combine(f.WorkPath, "Main.cs"),
            Path.Combine(f.WorkPath, "Views"));

        Assert.Empty(MainViewModel.AcceptedForDrop(f.WorkPath, items));
        Assert.False(f.ViewModel.CanDropOnto(f.WorkPath, items));
    }

    /// <summary>
    /// 일부만 목적지에서 왔으면 <b>그 항목만</b> 빠지고 나머지는 받는다 — 뭉치째 거절하지 않는다.
    /// </summary>
    [Fact]
    public async Task OnlyTheItemsThatCameFromTheDestinationAreDropped()
    {
        using var f = await ReadyAsync();

        var fromDocs = Path.Combine(f.DocsPath, "spec.md");
        var fromWork = Path.Combine(f.WorkPath, "Main.cs");
        var items = ShellItems(fromDocs, fromWork);

        var accepted = MainViewModel.AcceptedForDrop(f.DocsPath, items);

        Assert.Single(accepted);
        Assert.Equal(fromWork, accepted[0].Path);
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }

    [Fact]
    public async Task NothingFromTheDestinationMeansEverythingIsAccepted()
    {
        using var f = await ReadyAsync();

        var items = ShellItems(
            Path.Combine(f.WorkPath, "Main.cs"),
            Path.Combine(f.OutputPath, "a.dll"));

        Assert.Equal(2, MainViewModel.AcceptedForDrop(f.DocsPath, items).Count);
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }

    /// <summary>
    /// 탐색기 검색 결과처럼 서로 다른 폴더의 항목이 한 번에 온다 — 뭉치의 출처가 하나라는
    /// 전제가 셸 드롭에는 없다(§4.9).
    /// </summary>
    [Fact]
    public async Task ItemsFromThreeDifferentFoldersAreJudgedOneByOne()
    {
        using var f = await ReadyAsync();

        var items = ShellItems(
            Path.Combine(f.DocsPath, "spec.md"),                 // 목적지에서 왔다 → 빠진다
            Path.Combine(f.WorkPath, "Main.cs"),
            Path.Combine(f.OutputPath, "a.dll"),
            Path.Combine(f.CodePath, "Main.cs"));

        var accepted = MainViewModel.AcceptedForDrop(f.DocsPath, items);

        Assert.Equal(3, accepted.Count);
        Assert.DoesNotContain(accepted, i => i.Path == Path.Combine(f.DocsPath, "spec.md"));
    }

    /// <summary>실제로 옮겨 보면 빠진 항목의 원본이 그대로 남는다.</summary>
    [Fact]
    public async Task TheSkippedItemIsLeftAloneAndTheRestGoThrough()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(Path.Combine(f.DocsPath, "spec.md"), Path.Combine(f.WorkPath, "Main.cs")),
            copy: false);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "spec.md")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    /// <summary>건너뛴 건수는 결과에 적힌다 (§4.9) — 부분 결과이므로 §5.1 의 남는 등급이다.</summary>
    [Fact]
    public async Task TheSkippedCountIsWrittenIntoTheResult()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(Path.Combine(f.DocsPath, "spec.md"), Path.Combine(f.WorkPath, "Main.cs")),
            copy: true);

        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.Contains("온 곳과 같은 폴더 1", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);

        // 사유가 전부 같고 요약에 이미 적혀 있다 — 창까지 띄우지는 않는다.
        Assert.Empty(f.Prompt.Reports);
    }

    /// <summary>전부 받은 드롭은 요약이 사라지는 등급이고 ① 문구가 붙지 않는다.</summary>
    [Fact]
    public async Task ADropWithNothingSkippedSaysNothingAboutSkipping()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath, ShellItems(Path.Combine(f.WorkPath, "Main.cs")), copy: true);

        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.DoesNotContain("온 곳과 같은 폴더", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
    }

    // ── ② 폴더를 자기 자신이나 자기 하위로 — 뭉치째 거절 ────────────────────

    [Fact]
    public async Task AFolderCannotSwallowItsOwnDestination()
    {
        using var f = await ReadyAsync();

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        var inside = Path.Combine(recipe, "Controls");
        Directory.CreateDirectory(inside);

        var items = ShellItems(recipe);

        Assert.False(f.ViewModel.CanDropOnto(recipe, items));
        Assert.False(f.ViewModel.CanDropOnto(inside, items));
    }

    /// <summary>
    /// ② 는 <b>뭉치째</b> 거절이다 — 목적지를 삼키는 폴더가 하나 섞이면 같이 온 파일도 받지 않는다.
    /// ① 과 달리 판정 단위를 항목으로 바꾸지 않았다(§4.9 — "②·③ 은 그대로다").
    /// </summary>
    [Fact]
    public async Task OneSwallowingFolderRefusesTheWholeBunch()
    {
        using var f = await ReadyAsync();

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        var inside = Path.Combine(recipe, "Controls");
        Directory.CreateDirectory(inside);

        var items = ShellItems(recipe, Path.Combine(f.OutputPath, "a.dll"));

        Assert.Empty(MainViewModel.AcceptedForDrop(inside, items));
        Assert.False(f.ViewModel.CanDropOnto(inside, items));
    }

    // ── ③ 다른 조작이 도는 중 · 배치 편집 중 ─────────────────────────────────

    /// <summary>
    /// 조작이 도는 동안에는 받지 않는다. 누르는 시점을 재현 가능하게 하려고 조작이 진행을
    /// 처음 보고한 순간에 판정한다 — 시간에 기대지 않는다.
    /// </summary>
    [Fact]
    public async Task NothingIsAcceptedWhileAnotherOperationIsRunning()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 30);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        f.Rotating.SelectedItems = [.. f.Rotating.Items.Where(i => i.Name == "logs")];
        f.ViewModel.AddToTray(f.Row("문서"));

        var items = ShellItems(Path.Combine(f.OutputPath, "a.dll"));
        bool? whileRunning = null;

        void JudgeWhileRunning(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText))
            {
                whileRunning ??= f.ViewModel.CanDropOnto(f.DocsPath, items);
            }
        }

        f.ViewModel.PropertyChanged += JudgeWhileRunning;
        try
        {
            await f.ViewModel.CopySelectionAsync();
        }
        finally
        {
            f.ViewModel.PropertyChanged -= JudgeWhileRunning;
        }

        Assert.False(whileRunning);

        // 끝난 뒤에는 다시 받는다.
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }

    /// <summary>
    /// 배치 편집 중에는 받지 않는다 — 편집 중에는 파일 목록이 잠긴다(§3.2). 타일은 히트테스트
    /// 에서도 빠지지만 왼쪽 폴더 행과 대상함은 살아 있어 판정에서 막아야 한다.
    /// </summary>
    [Fact]
    public async Task NothingIsAcceptedWhileTheLayoutIsBeingEdited()
    {
        using var f = await ReadyAsync();
        var items = ShellItems(Path.Combine(f.WorkPath, "Main.cs"));

        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));

        f.ViewModel.ToggleLayoutEdit();
        Assert.False(f.ViewModel.CanDropOnto(f.DocsPath, items));

        // 파일도 움직이지 않는다.
        await f.ViewModel.DropOntoPathAsync(f.DocsPath, items, copy: false);
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));

        f.ViewModel.ToggleLayoutEdit();
        Assert.True(f.ViewModel.CanDropOnto(f.DocsPath, items));
    }

    // ── 빈 목록 · 닿지 못하는 목적지 ─────────────────────────────────────────

    [Fact]
    public async Task AnEmptyShellDropIsNotAccepted()
    {
        using var f = await ReadyAsync();

        Assert.False(f.ViewModel.CanDropOnto(f.DocsPath, []));
        Assert.False(f.ViewModel.CanDropOnto(null, ShellItems(Path.Combine(f.WorkPath, "Main.cs"))));
        Assert.False(f.ViewModel.CanDropOnto(f.DocsPath, null));
    }

    /// <summary>
    /// 과녁 판정은 I/O 없이 문자열로 끝낸다 — 닿을 수 없는 목적지도 과녁은 켜지고, 실제로
    /// 놓았을 때 이유가 상태 바에 남는다(§5). 내부 드래그와 같은 자리다.
    /// </summary>
    [Fact]
    public async Task AnUnreachableDestinationIsRefusedWhenItIsActuallyDroppedOn()
    {
        using var f = await ReadyAsync();
        var items = ShellItems(Path.Combine(f.WorkPath, "Main.cs"));

        Assert.True(f.ViewModel.CanDropOnto(MainWindowFixture.UnreachablePath, items));

        await f.ViewModel.DropOntoPathAsync(MainWindowFixture.UnreachablePath, items, copy: false);

        Assert.Contains("목적지에 닿지 못했다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);

        // 이동이었지만 원본은 그대로다.
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    // ── 셸이 주는 것은 경로뿐 — 폴더도 통째로 온다 ───────────────────────────

    [Fact]
    public async Task AFolderDraggedInFromTheShellIsCopiedWhole()
    {
        using var f = await ReadyAsync();

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath, ShellItems(Path.Combine(f.WorkPath, "Recipe")), copy: true);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs")));

        // 복사였으니 원본은 남는다 — 셸 드롭의 기본은 복사다(§4.9).
        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
    }

    // ── 타일의 목적지는 앵커가 아니라 보고 있는 경로다 (§4.9) ────────────────

    [Fact]
    public async Task ATileThatWentIntoASubfolderAcceptsIntoThatSubfolder()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        Assert.Equal(f.WorkPath, f.Rotating.ShellDropDestination);

        // 하위 폴더로 들어가면 목적지가 따라 내려간다 — 앵커에 머물지 않는다.
        await f.Rotating.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(recipe, f.Rotating.ShellDropDestination);
        Assert.NotEqual(f.Rotating.Entry!.Path, f.Rotating.ShellDropDestination);

        await f.ViewModel.DropOntoPathAsync(
            f.Rotating.ShellDropDestination!,
            ShellItems(Path.Combine(f.OutputPath, "a.dll")),
            copy: true);

        Assert.True(File.Exists(Path.Combine(recipe, "a.dll")));
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "a.dll")));
    }

    /// <summary>아직 아무 폴더도 안 띄운 순환 칸은 놓을 자리가 아니다.</summary>
    [Fact]
    public void AnEmptyTileIsNotADropTarget()
    {
        using var f = new MainWindowFixture();

        Assert.Null(f.Rotating.ShellDropDestination);
    }

    /// <summary>
    /// 접근 불가 폴더를 보고 있는 타일은 받지 않는다 — 목록에서 지우지는 않되(§5) 놓을 자리도 아니다.
    /// </summary>
    [Fact]
    public async Task ATileShowingAnUnreachableFolderIsNotADropTarget()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        Assert.True(f.Rotating.HasFailure);
        Assert.Null(f.Rotating.ShellDropDestination);
    }

    /// <summary>타일은 셸 드롭 통로를 들고 있다 — 패널은 엔진을 모른다 (§4.4 와 같은 결).</summary>
    [Fact]
    public void EveryTilePanelHasTheShellDropHostWired()
    {
        using var f = new MainWindowFixture();

        Assert.All(f.ViewModel.Panels, p => Assert.Same(f.ViewModel, p.ShellDropHost));
    }

    // ── 두 포맷이 함께 실린 드롭 — 받는 쪽의 순서 (§4.9 · §11) ───────────────
    //
    // 손으로는 이 Stage 에서 확인할 수 없다 — 내보내기(Stage 2)가 없어 두 포맷이 함께 실린
    // 드롭을 만들 수 없다. 순서 규칙은 여기서 덮는다.

    [Fact]
    public void APrivateFormatDropTakesTheInternalRuleAndStopsThere()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, new FileDropPayload("C:\\x", []));

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
    }

    /// <summary>
    /// 두 포맷이 <b>다 실려</b> 있어도 사설 포맷이 이긴다 — 셸 분기로 내려가지 않는다.
    /// 두 분기가 같은 드롭을 둘 다 물면 파일이 두 번 처리된다.
    /// </summary>
    [Fact]
    public void BothFormatsOnOneDropStillTakeTheInternalRule()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, new FileDropPayload("C:\\x", []));
        data.SetData(DataFormats.FileDrop, new[] { "C:\\x\\a.txt" });

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
    }

    [Fact]
    public void AShellOnlyDropTakesTheShellRule()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { "C:\\x\\a.txt" });

        Assert.Equal(DropRoute.Shell, DropRouter.Route(data));
    }

    [Fact]
    public void ADropWithNeitherFormatIsNotRouted()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "그냥 글자");

        Assert.Equal(DropRoute.None, DropRouter.Route(data));
        Assert.Equal(DropRoute.None, DropRouter.Route(null));
    }

    // ── 셸 드롭의 수정키 — 기본 복사, Shift 이동 (§4.9, 내부와 반대) ─────────

    [Fact]
    public void AShellDropCopiesByDefaultAndMovesWithShift()
    {
        Assert.True(DropRouter.ShellDropCopies(DragDropKeyStates.None));
        Assert.False(DropRouter.ShellDropCopies(
            DragDropKeyStates.ShiftKey));

        // Ctrl 은 셸 드롭에서 아무 뜻이 없다 — 내부 규칙의 키다. 여전히 복사여야 한다.
        Assert.True(DropRouter.ShellDropCopies(
            DragDropKeyStates.ControlKey));
    }

    /// <summary>
    /// 셸이 <c>Copy, Move, Link</c> 를 다 허용해도 우리가 정한 하나로 좁혀 돌려준다 —
    /// <c>Link</c> 는 쓰지 않는다 (§4.9).
    /// </summary>
    [Fact]
    public void TheEffectIsNarrowedToOneAndNeverLink()
    {
        Assert.Equal(
            DragDropEffects.Copy,
            DropRouter.ShellEffect(DragDropKeyStates.None));

        Assert.Equal(
            DragDropEffects.Move,
            DropRouter.ShellEffect(DragDropKeyStates.ShiftKey));
    }

    /// <summary>셸이 준 경로 목록이 엔진 인자로 변환 없이 들어간다 — 폴더 여부만 디스크에 묻는다.</summary>
    [Fact]
    public async Task ShellPathsBecomeOperationItemsWithNoConversionLayer()
    {
        using var f = await ReadyAsync();

        var data = new DataObject();
        data.SetData(
            DataFormats.FileDrop,
            new[] { Path.Combine(f.WorkPath, "Main.cs"), Path.Combine(f.WorkPath, "Recipe") });

        var items = DropRouter.ShellItems(data);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Path == Path.Combine(f.WorkPath, "Main.cs") && !i.IsDirectory);
        Assert.Contains(items, i => i.Path == Path.Combine(f.WorkPath, "Recipe") && i.IsDirectory);
    }

    /// <summary>
    /// 수정키 없는 셸 드롭은 <b>원본을 남긴다.</b> 규칙(<see cref="DropRouter.ShellDropCopies"/>)과
    /// 실행(<see cref="MainViewModel.DropOntoPathAsync"/>)을 이어 붙여 확인한다 — 이 둘이
    /// 어긋나면 한 번의 드래그가 앱이 관리하지 않는 폴더의 파일을 지운다(§4.9 가 막으려던 사고).
    /// </summary>
    [Fact]
    public async Task AShellDropWithNoModifierLeavesTheOriginalWhereItWas()
    {
        using var f = await ReadyAsync();
        var source = Path.Combine(f.WorkPath, "Main.cs");

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(source),
            DropRouter.ShellDropCopies(DragDropKeyStates.None));

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    /// <summary><c>Shift</c> 를 누른 셸 드롭만 원본을 옮긴다.</summary>
    [Fact]
    public async Task AShellDropWithShiftMovesTheOriginal()
    {
        using var f = await ReadyAsync();
        var source = Path.Combine(f.WorkPath, "Main.cs");

        await f.ViewModel.DropOntoPathAsync(
            f.DocsPath,
            ShellItems(source),
            DropRouter.ShellDropCopies(DragDropKeyStates.ShiftKey));

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    // ── 내부 드래그와 같은 함수를 쓴다 (§3-A 통일) ───────────────────────────

    /// <summary>
    /// 내부 드래그의 답은 대개 달라지지 않는다 — 뭉치의 출처가 패널 하나여서(§4.8) 모든 항목의
    /// 부모가 같고, 항목 단위 판정이 전부 빼서 0개가 되어 예전의 뭉치 단위 거절과 같은 결론이 된다.
    /// </summary>
    [Fact]
    public async Task AnInternalDragIntoItsOwnFolderIsStillRefused()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        f.Rotating.SelectedItems = [.. f.Rotating.Items.Where(i => i.Name == "Main.cs")];

        var payload = f.Rotating.BuildDragPayload()!;

        Assert.False(f.ViewModel.CanDropOnto(f.Row("작업").Entry, payload));
        Assert.False(f.ViewModel.CanDropOnto(f.WorkPath, payload.Items));
    }

    /// <summary>
    /// <b>다만 예외가 하나 있다.</b> 재귀 검색 중에는 한 타일의 선택에도 <b>서로 다른 폴더</b>의
    /// 항목이 담긴다 — 그래서 "뭉치의 출처가 패널 하나"라는 전제가 내부 드래그에서도 항상
    /// 참은 아니다. 항목 단위 판정은 이 경우를 옛 뭉치 단위 판정보다 <b>정확하게</b> 답한다:
    /// 예전에는 <c>SourceFolder</c> 하나가 목적지와 같으면 하위 폴더에서 온 항목까지 통째로
    /// 거절됐다. §4.9 가 ① 을 항목 단위로 못박은 이유가 그대로 내부 경로에도 적용된다.
    /// </summary>
    [Fact]
    public async Task RecursiveSearchResultsAreJudgedByTheirOwnParentNotTheTilePath()
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

        panel.SelectedItems = [.. panel.DisplayItems.Where(i => i.Name == "NumberBox.xaml")];
        var payload = panel.BuildDragPayload()!;

        // 뭉치의 "출처"는 타일이 보고 있는 경로지만, 항목은 그 하위에서 왔다.
        Assert.Equal(f.WorkPath, payload.SourceFolder);
        Assert.Equal(
            Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml"),
            payload.Items.Single().Path);

        // 항목의 부모가 목적지와 다르므로 받는다 — 뭉치 단위로 보면 거절됐을 자리다.
        Assert.True(f.ViewModel.CanDropOnto(f.WorkPath, payload.Items));
        Assert.Single(MainViewModel.AcceptedForDrop(f.WorkPath, payload.Items));
    }

    private static void FillTree(string root, int count)
    {
        Directory.CreateDirectory(root);
        for (var i = 0; i < count; i++)
        {
            File.WriteAllText(Path.Combine(root, $"f{i}.log"), new string('x', 64));
        }
    }
}
