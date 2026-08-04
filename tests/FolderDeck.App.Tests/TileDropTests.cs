using System.ComponentModel;
using System.Windows;
using FolderDeck.App.Views;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일에서 타일로 (§4.2-2 · Phase 5 Stage 3).
/// </summary>
/// <remarks>
/// Stage 1 이 타일을 셸 드롭의 과녁으로 만들었고 Stage 3 이 <b>분기 하나</b>를 더했다 — 앱 안에서
/// 시작한 드래그도 타일로 놓는다. 그 분기가 갈리는 자리는 <see cref="DropRouter.Resolve"/> 이고,
/// 목적지는 타일이 <b>보고 있는 경로</b>, ①②③ 판정과 실행은 <c>MainViewModel</c> 이다 — 그 셋을
/// 이어 붙여 시험한다.
/// <para>
/// 실제 마우스 제스처는 흉내 내지 않는다(<see cref="ShellDropTests"/> · <see cref="DragDropTests"/>
/// 와 같은 경계). 대신 <b>끄는 쪽이 정말로 싣는 것</b>으로 판정한다 — 타일에서 시작한 드래그는
/// 사설 포맷과 셸 포맷을 둘 다 싣기 때문에(Stage 2) 흉내 낸 <c>DataObject</c> 로는 순서 규칙이
/// 걸리는지 알 수 없다.
/// </para>
/// </remarks>
public sealed class TileDropTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    /// <summary>이 경로를 보고 있는 타일. 상시 칸의 순서를 짐작하지 않는다.</summary>
    private static FolderPanelViewModel Tile(MainWindowFixture f, string path) =>
        f.ViewModel.Panels.Single(p => p.ShellDropDestination == path);

    /// <summary>
    /// 타일에서 시작한 드래그가 <b>실제로</b> 싣는 것 — 사설 포맷과 셸 포맷 둘 다다(Stage 2).
    /// </summary>
    private static DataObject Dragged(FolderPanelViewModel from)
    {
        var payload = from.BuildDragPayload()!;
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, payload);
        ShellExport.Attach(data, payload.Items);
        return data;
    }

    /// <summary>타일의 드롭 판정 함수가 하는 일 그대로: 풀고, 이 타일이 받는지 묻고, 받는다.</summary>
    private static async Task<bool> DropOntoTileAsync(
        MainWindowFixture f, FolderPanelViewModel tile, DataObject data, DragDropKeyStates keys)
    {
        if (Accepted(f, tile, data, keys) is not { } incoming)
        {
            return false;
        }

        await f.ViewModel.DropOntoPathAsync(
            tile.ShellDropDestination!, incoming.Items, incoming.Copy);
        return true;
    }

    /// <summary>과녁이 켜지는가 — 파일을 움직이지 않고 판정만 본다.</summary>
    private static (IReadOnlyList<OperationItem> Items, bool Copy)? Accepted(
        MainWindowFixture f, FolderPanelViewModel tile, DataObject data, DragDropKeyStates keys) =>
        tile.ShellDropDestination is { } destination
        && DropRouter.Resolve(data, keys) is { } resolved
        && f.ViewModel.CanDropOnto(destination, resolved.Items)
            ? resolved
            : null;

    private static bool Accepts(
        MainWindowFixture f, FolderPanelViewModel tile, DataObject data, DragDropKeyStates keys) =>
        Accepted(f, tile, data, keys) is not null;

    // ── 기본 이동 · Ctrl 복사 (§4.2-2 — 셸 받기와 일부러 반대다) ──────────────

    /// <summary>수정키 없이 타일에서 타일로 놓으면 <b>이동</b>이다 — 원본이 사라진다.</summary>
    [Fact]
    public async Task DroppingFromOneTileOntoAnotherMovesByDefault()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        Assert.True(await DropOntoTileAsync(
            f, Tile(f, f.OutputPath), Dragged(f.Rotating), DragDropKeyStates.None));

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));

        // 결과가 상태 바에 적힌다 — "밖으로 넘겼다"가 아니다 (§4.2-2).
        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.DoesNotContain("앱 밖으로", f.ViewModel.Message);

        // 확인창은 없다 (§4.2-2). 이름도 겹치지 않았으니 충돌 창도 없다.
        Assert.Empty(f.Prompt.Messages);
        Assert.Empty(f.Prompt.ConflictMessages);
    }

    /// <summary><c>Ctrl</c> 을 누르고 놓으면 복사다 — 원본이 남는다.</summary>
    [Fact]
    public async Task DroppingWithControlCopiesAndLeavesTheOriginal()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        Assert.True(await DropOntoTileAsync(
            f, Tile(f, f.OutputPath), Dragged(f.Rotating), DragDropKeyStates.ControlKey));

        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    /// <summary>폴더째도, 여러 개도 간다.</summary>
    [Fact]
    public async Task AFolderAndSeveralItemsGoTogether()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs", "Recipe", "Views");

        Assert.True(await DropOntoTileAsync(
            f, Tile(f, f.OutputPath), Dragged(f.Rotating), DragDropKeyStates.None));

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Recipe", "Recipe.cs")));
        Assert.True(Directory.Exists(Path.Combine(f.OutputPath, "Views")));
        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
    }

    /// <summary>
    /// 목적지는 타일이 <b>보고 있는 경로</b>다 — 하위 폴더로 들어간 타일에 놓으면 거기로 간다.
    /// 앵커가 아니다(§4.9 와 같은 기준).
    /// </summary>
    [Fact]
    public async Task TheDestinationIsWhatTheTileIsShowingNotItsAnchor()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");

        var views = Path.Combine(f.WorkPath, "Views");
        Assert.Equal(views, f.Rotating.ShellDropDestination);
        Assert.NotEqual(f.Rotating.Entry!.Path, f.Rotating.ShellDropDestination);

        Assert.True(await DropOntoTileAsync(f, f.Rotating, Dragged(code), DragDropKeyStates.None));

        Assert.True(File.Exists(Path.Combine(views, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }

    // ── 거절 조합 ①②③ — Stage 1 이 통일해 둔 함수가 그대로 답한다 ───────────

    /// <summary>
    /// <b>같은 타일 안</b>에 놓으면 받지 않는다 — 과녁도 켜지지 않는다. 막는 코드가 따로 없다:
    /// 항목의 부모가 목적지와 같아 ① 이 전부 빼서 0개가 된다.
    /// </summary>
    [Fact]
    public async Task DroppingBackIntoTheSameTileIsRefused()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs", "Recipe");

        Assert.False(Accepts(f, f.Rotating, Dragged(f.Rotating), DragDropKeyStates.None));
        Assert.False(Accepts(f, f.Rotating, Dragged(f.Rotating), DragDropKeyStates.ControlKey));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    /// <summary>
    /// <b>두 타일이 같은 폴더</b>를 볼 때 서로 끌어도 받지 않는다 — 판정이 경로로 이뤄지므로
    /// 타일이 둘인 것은 답에 영향을 주지 않는다.
    /// </summary>
    [Fact]
    public async Task TwoTilesShowingTheSameFolderRefuseEachOther()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);

        // 순환 칸을 code 폴더로 몰아 놓는다 — 이제 두 타일이 같은 곳을 본다.
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(f.CodePath);
        Assert.Equal(code.ShellDropDestination, f.Rotating.ShellDropDestination);

        Select(code, "Main.cs");
        var data = Dragged(code);

        Assert.False(Accepts(f, f.Rotating, data, DragDropKeyStates.None));
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }

    /// <summary>② 폴더를 <b>자기 하위</b>를 보고 있는 타일로 끌면 뭉치째 거절이다.</summary>
    [Fact]
    public async Task AFolderCannotBeDroppedIntoATileShowingItsOwnSubfolder()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Recipe");
        var data = Dragged(code);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(Path.Combine(f.CodePath, "Recipe"));

        Assert.False(Accepts(f, f.Rotating, data, DragDropKeyStates.None));
        Assert.True(Directory.Exists(Path.Combine(f.CodePath, "Recipe")));

        // 같이 온 파일이 있어도 뭉치째 거절이다 — ② 는 항목 단위가 아니다(§4.9).
        Select(code, "Recipe", "Main.cs");
        Assert.False(Accepts(f, f.Rotating, Dragged(code), DragDropKeyStates.None));
    }

    /// <summary>③ 다른 조작이 도는 중에는 타일도 받지 않는다.</summary>
    [Fact]
    public async Task ATileDoesNotAcceptWhileAnotherOperationIsRunning()
    {
        using var f = await ReadyAsync();
        FillTree(Path.Combine(f.WorkPath, "logs"), 30);

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        var data = Dragged(code);

        // 오래 도는 조작 하나를 띄운다 — 순환 칸의 logs 를 대상함으로 복사.
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "logs");
        f.ViewModel.AddToTray(f.Row("문서"));

        bool? whileRunning = null;

        void JudgeWhileRunning(object? _, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.ProgressText))
            {
                whileRunning ??= Accepts(f, f.Rotating, data, DragDropKeyStates.None);
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
        Assert.True(await DropOntoTileAsync(f, f.Rotating, data, DragDropKeyStates.None));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    /// <summary>배치 편집 중에는 받지 않는다 — 타일은 히트테스트에서도 빠지지만 판정도 막는다.</summary>
    [Fact]
    public async Task ATileDoesNotAcceptWhileTheLayoutIsBeingEdited()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        var data = Dragged(f.Rotating);
        var output = Tile(f, f.OutputPath);

        Assert.True(Accepts(f, output, data, DragDropKeyStates.None));

        f.ViewModel.ToggleLayoutEdit();
        Assert.False(Accepts(f, output, data, DragDropKeyStates.None));

        f.ViewModel.ToggleLayoutEdit();
        Assert.True(Accepts(f, output, data, DragDropKeyStates.None));
    }

    // ── 검색 결과가 섞인 뭉치 (§4.10) ────────────────────────────────────────

    /// <summary>
    /// <b>재귀 검색 결과를 자기 타일에 놓으면 받는다</b> — 하위 폴더 항목의 부모가 목적지와
    /// 다르므로 ① 에 걸리지 않는다. 상위 폴더를 보며 검색한 결과를 그 상위 폴더에 놓으면
    /// 하위 것들이 모인다 — §4.2-2 가 적어 둔 동작이라 막지 않는다.
    /// <para>
    /// 같은 뭉치에 목적지에서 <b>바로</b> 온 항목이 섞여 있으면 그것만 빠지고 건수가 결과에
    /// 적힌다(§4.9) — ① 의 판정 단위가 뭉치가 아니라 항목이기 때문이다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task RecursiveSearchResultsDroppedOntoTheirOwnTileAreGatheredUp()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();

        // 목적지에서 바로 온 항목 하나를 검색에 걸리게 심는다.
        File.WriteAllText(Path.Combine(f.WorkPath, "NumberTop.cs"), "x");

        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        var panel = f.Rotating;
        panel.IncludeSubfolders = true;
        panel.SearchText = "Number";

        for (var i = 0; i < 200 && (panel.IsSearching || panel.DisplayItems.Count < 4); i++)
        {
            await Task.Delay(25);
        }

        panel.SelectedItems = [.. panel.DisplayItems];

        var deep = Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml");
        Assert.True(File.Exists(deep));
        Assert.Contains(panel.SelectedItems, i => i.FullPath == deep);
        Assert.Contains(panel.SelectedItems, i => i.FullPath == Path.Combine(f.WorkPath, "NumberTop.cs"));

        Assert.True(await DropOntoTileAsync(f, panel, Dragged(panel), DragDropKeyStates.None));

        // 하위에 있던 것은 타일이 보고 있는 경로로 올라왔다.
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "NumberBox.xaml")));
        Assert.False(File.Exists(deep));

        // 목적지에서 바로 온 것은 건드리지 않고 건수만 적었다.
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "NumberTop.cs")));
        Assert.Contains("온 곳과 같은 폴더 1", f.ViewModel.Message);
    }

    // ── 두 규칙이 갈리는 자리 — DropRouter.Resolve (§4.9 순서 규칙 위) ────────

    /// <summary>
    /// 타일에서 시작한 드래그는 <b>두 포맷을 다 싣는다</b>(Stage 2). 그것이 앱 안에 놓이면
    /// 사설 포맷이 이기고 <b>내부 규칙</b>(기본 이동)이 된다 — 여기가 뒤집히면 앱 안의 이동이
    /// 조용히 복사가 되거나 그 반대가 된다.
    /// </summary>
    [Fact]
    public async Task ADragThatCarriesBothFormatsTakesTheInternalRuleWhenItLandsInside()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        var data = Dragged(f.Rotating);

        Assert.True(data.GetDataPresent(DataFormats.FileDrop));
        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));

        // 수정키가 없으면 이동 — 셸 규칙(기본 복사)을 탔다면 true 가 됐을 자리다.
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.None)!.Value.Copy);

        // Shift 는 내부 규칙에서 아무 뜻이 없다 — 여전히 이동이다.
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.ShiftKey)!.Value.Copy);

        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.ControlKey)!.Value.Copy);
    }

    [Fact]
    public void TheInternalRuleMovesByDefaultAndCopiesWithControl()
    {
        Assert.False(DropRouter.InternalDropCopies(DragDropKeyStates.None));
        Assert.True(DropRouter.InternalDropCopies(DragDropKeyStates.ControlKey));

        // Shift 는 셸 규칙의 키다 — 내부에서는 아무 뜻이 없다.
        Assert.False(DropRouter.InternalDropCopies(DragDropKeyStates.ShiftKey));
    }

    /// <summary>사설 포맷이 실린 드롭은 항목을 <b>그대로</b> 낸다 — 변환 계층이 없다.</summary>
    [Fact]
    public void AnInternalDropCarriesItsItemsWithNoConversion()
    {
        var items = new[]
        {
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Views", true),
        };

        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, new FileDropPayload(@"C:\deck\work", items));

        Assert.Same(items, DropRouter.Resolve(data, DragDropKeyStates.None)!.Value.Items);
    }

    /// <summary>
    /// 셸에서만 온 드롭은 셸 규칙 그대로다 — <b>A 의 회귀 지점이다.</b> 같은 판정 함수를 두
    /// 경로가 쓰므로 분기를 더하다 이쪽이 망가지기 쉽다.
    /// </summary>
    [Fact]
    public void AShellOnlyDropStillTakesTheShellRule()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { @"C:\outside\a.txt" });

        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.None)!.Value.Copy);
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.ShiftKey)!.Value.Copy);

        // Ctrl 은 셸 규칙에서 아무 뜻이 없다 — 여전히 복사다.
        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.ControlKey)!.Value.Copy);
    }

    [Fact]
    public void ADropWithNeitherFormatIsNotResolved()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "그냥 글자");

        Assert.Null(DropRouter.Resolve(data, DragDropKeyStates.None));
        Assert.Null(DropRouter.Resolve(null, DragDropKeyStates.None));
    }

    /// <summary>사설 포맷 자리에 엉뚱한 것이 실려 있으면 받지 않는다 — 셸 분기로 흘러가지도 않는다.</summary>
    [Fact]
    public void APrivateFormatWithTheWrongPayloadIsNotResolved()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, "뭉치가 아니다");
        data.SetData(DataFormats.FileDrop, new[] { @"C:\outside\a.txt" });

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
        Assert.Null(DropRouter.Resolve(data, DragDropKeyStates.None));
    }

    /// <summary>커서 효과는 우리가 정한 하나로 좁힌다 — <c>Link</c> 는 쓰지 않는다(§4.9).</summary>
    [Fact]
    public void TheEffectIsOneOfTwoAndNeverLink()
    {
        Assert.Equal(DragDropEffects.Copy, DropRouter.Effect(copy: true));
        Assert.Equal(DragDropEffects.Move, DropRouter.Effect(copy: false));
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
