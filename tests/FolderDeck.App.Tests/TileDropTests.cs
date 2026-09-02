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
        data.SetData(FolderPanelView.FileDragFormat, DragPayloadCodec.Pack(payload));
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
        && DropRouter.Resolve(data, keys, destination) is { } resolved
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

        // 목적지는 픽스처 안이라 원본과 같은 볼륨이다 — 볼륨이 가르는 자리를 여기서 섞지 않는다.
        var destination = f.OutputPath;

        // 수정키가 없고 같은 볼륨이면 이동 — 셸 규칙(기본 복사)을 탔다면 true 가 됐을 자리다.
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.None, destination)!.Value.Copy);

        // Shift 는 내부 규칙에서 "이동"이다 — 같은 볼륨에서는 무수정키와 결과가 같다.
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.ShiftKey, destination)!.Value.Copy);

        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.ControlKey, destination)!.Value.Copy);
    }

    /// <summary>
    /// 내부 규칙의 네 줄 (§4.2-2 · D51 결정 1) — <c>Ctrl</c> 복사 · <c>Shift</c> 이동 ·
    /// <c>Ctrl</c>+<c>Shift</c> 복사 · <b>수정키가 없으면 볼륨이 가른다.</b>
    /// </summary>
    /// <remarks>
    /// 이 판은 전에 <c>TheInternalRuleMovesByDefaultAndCopiesWithControl</c> 이었고
    /// 「내부 규칙은 <c>Shift</c> 를 읽지 않는다」를 <b>명시적으로 굳혀 두었다</b> — 이번 회차가
    /// 바꾸기로 한 바로 그 동작이라 이름이 그대로면 시험이 거짓말을 한다.
    /// </remarks>
    [Theory]
    // 수정키 없음 — 같은 볼륨이면 이동, 다른 볼륨이면 복사. 이 회차의 본체다.
    [InlineData(DragDropKeyStates.None, true, false)]
    [InlineData(DragDropKeyStates.None, false, true)]
    // Ctrl 은 볼륨과 무관하게 복사.
    [InlineData(DragDropKeyStates.ControlKey, true, true)]
    [InlineData(DragDropKeyStates.ControlKey, false, true)]
    // Shift 는 볼륨과 무관하게 이동 — 전에는 읽지 않던 키다.
    [InlineData(DragDropKeyStates.ShiftKey, true, false)]
    [InlineData(DragDropKeyStates.ShiftKey, false, false)]
    // Ctrl+Shift 는 복사 — 탐색기의 바로가기 만들기가 이 앱에 없어 되돌릴 수 있는 쪽으로 떨어뜨린다.
    [InlineData(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey, true, true)]
    [InlineData(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey, false, true)]
    public void TheInternalRuleReadsControlShiftAndTheVolume(
        DragDropKeyStates keys, bool sameVolume, bool copies) =>
        Assert.Equal(copies, DropRouter.InternalDropCopies(keys, sameVolume));

    /// <summary>
    /// <b>셸 규칙은 이 회차에서 움직이지 않았다</b> (§4.9 · D51 결정 8) — 밖에서 들어오는 것의
    /// 기본 복사는 취향이 아니라 방어이고 <b>볼륨과 무관하게</b> 성립한다.
    /// </summary>
    /// <remarks>
    /// 내부 규칙을 고치다 셸 가지가 함께 움직이면 앱이 관리하지 않는 폴더의 원본이 지워진다.
    /// 셸 규칙은 볼륨을 아예 인자로 받지 않는다는 것 자체가 단언이다.
    /// </remarks>
    [Fact]
    public void TheShellRuleDidNotMove()
    {
        Assert.True(DropRouter.ShellDropCopies(DragDropKeyStates.None));
        Assert.False(DropRouter.ShellDropCopies(DragDropKeyStates.ShiftKey));
        Assert.True(DropRouter.ShellDropCopies(DragDropKeyStates.ControlKey));
        Assert.False(
            DropRouter.ShellDropCopies(DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey));
    }

    /// <summary>
    /// 사설 포맷이 실린 드롭은 항목을 <b>그대로</b> 낸다 — 값이 바뀌지 않는다.
    /// </summary>
    /// <remarks>
    /// 예전 이름은 <c>AnInternalDropCarriesItsItemsWithNoConversion</c> 이었고 <c>Assert.Same</c>
    /// 으로 <b>참조</b> 동일성까지 지켰다. Phase 14 D77-b 가 사설 포맷에 객체 대신 글자(JSON)를
    /// 싣게 되면서 그 불변은 실제로 죽었다 — <c>Pack</c>·<c>Unpack</c> 왕복은 구조적으로 항상
    /// 새 객체를 낸다. 죽은 불변을 지키는 시험을 남기지 않는다 — 이름과 단정을 남은 불변
    /// (값이 같다)에 맞춰 다시 적는다.
    /// </remarks>
    [Fact]
    public void AnInternalDropCarriesItsItemsUnchanged()
    {
        var items = new[]
        {
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Views", true),
        };

        var data = new DataObject();
        data.SetData(
            FolderPanelView.FileDragFormat,
            DragPayloadCodec.Pack(new FileDropPayload(@"C:\deck\work", items)));

        Assert.Equal(
            items, DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out")!.Value.Items);
    }

    // ── 뭉치 판정 (§4.2-2 · D51 결정 2) ──────────────────────────────────────
    //
    // 판정 단위는 <b>뭉치</b>다. 항목마다 복사/이동을 가르면 한 번 누른 것이 조작 둘 ·
    // 결과 보고 둘이 되고 뒤가 앞을 덮는다 — §4.3 이 같은 이유로 이미 정해 둔 것이다.
    // 볼륨이 섞이는 경로는 재귀 검색 결과뿐이다(§4.10).

    /// <summary>여럿이 다 목적지와 같은 볼륨이면 뭉치는 같은 볼륨이다.</summary>
    [Fact]
    public void ABundleEntirelyOnTheDestinationVolumeCountsAsSameVolume() =>
        Assert.True(DropRouter.AllOnSameVolume(
            [
                new OperationItem(@"C:\deck\work\Main.cs", false),
                new OperationItem(@"C:\deck\other\Views", true),
            ],
            @"C:\deck\out"));

    /// <summary>
    /// <b>하나라도 다른 볼륨이면 뭉치 전부가 다른 볼륨이다</b> — 그래서 무수정키 드롭이 통째로
    /// 복사가 되고 안전한 쪽으로 떨어진다. 항목마다 가르지 않는다.
    /// </summary>
    [Fact]
    public void OneItemOnAnotherVolumeMakesTheWholeBundleCopy()
    {
        IReadOnlyList<OperationItem> mixed =
        [
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Recipe", true),
            new OperationItem(@"D:\elsewhere\one.txt", false),   // 이 하나가 뭉치를 가른다
        ];

        Assert.False(DropRouter.AllOnSameVolume(mixed, @"C:\deck\out"));

        // 그래서 수정키 없는 드롭은 뭉치 전부가 복사다.
        Assert.True(DropRouter.InternalDropCopies(
            DragDropKeyStates.None, DropRouter.AllOnSameVolume(mixed, @"C:\deck\out")));
    }

    /// <summary>목적지를 알 수 없으면 같은 볼륨이라고 말하지 않는다 — 모를 때도 되돌릴 수 있는 쪽이다.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnknownDestinationIsNotTheSameVolume(string? destination) =>
        Assert.False(DropRouter.AllOnSameVolume(
            [new OperationItem(@"C:\deck\work\Main.cs", false)], destination));

    // ── 과녁 문구 (§4.2-2 259행 · D51 결정 5) ────────────────────────────────

    /// <summary>
    /// <b>문구가 결과를 말한다.</b> 규칙이 조건부가 된 이상 손동작만으로는 무엇이 될지 알 수 없고
    /// 이 경로에는 확인창을 두지 않기로 했다 — 놓기 전에 말하는 자리는 과녁뿐이다.
    /// </summary>
    [Fact]
    public void TheTargetCaptionSaysWhatTheDropWillDo()
    {
        Assert.Equal("여기로 복사", DropTargetCaption.For(copy: true));
        Assert.Equal("여기로 이동", DropTargetCaption.For(copy: false));
    }

    /// <summary>
    /// 받는 자리 <b>셋</b>(왼쪽 폴더 행 · 대상함 · 타일)이 <b>같은 값</b>을 읽는다 — 글귀를 XAML 에
    /// 박으면 세 벌이 되어 하나만 고쳐진다.
    /// </summary>
    [Fact]
    public async Task TheThreeTargetsReadTheSameCaption()
    {
        using var f = await ReadyAsync();

        f.ViewModel.AddToTray(f.Row("산출물"));

        var row = f.Row("작업");
        var tray = f.ViewModel.Tray.Single();
        var tile = f.Rotating;

        foreach (var copy in new[] { false, true })
        {
            row.DropTargetCopies = copy;
            tray.DropTargetCopies = copy;
            tile.DropTargetCopies = copy;

            Assert.Equal(DropTargetCaption.For(copy), row.DropTargetText);
            Assert.Equal(DropTargetCaption.For(copy), tray.DropTargetText);
            Assert.Equal(DropTargetCaption.For(copy), tile.DropTargetText);
        }
    }

    /// <summary>
    /// 이동에서 복사로 갈리면 <b>화면이 따라와야 한다</b> — 알림이 없으면 끌고 다니는 동안 글귀가
    /// 옛 값으로 굳는다.
    /// </summary>
    [Fact]
    public async Task TheCaptionRaisesChangeWhenTheVerdictFlips()
    {
        using var f = await ReadyAsync();

        var row = f.Row("작업");
        var seen = new List<string?>();
        ((INotifyPropertyChanged)row).PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        row.DropTargetCopies = true;

        Assert.Contains(nameof(FolderRowViewModel.DropTargetText), seen);
        Assert.Equal("여기로 복사", row.DropTargetText);
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

        // 목적지는 셸 가지가 아예 보지 않는다 — 볼륨과 무관하게 기본 복사다(결정 8).
        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\outside")!.Value.Copy);
        Assert.False(DropRouter.Resolve(data, DragDropKeyStates.ShiftKey, @"C:\outside")!.Value.Copy);

        // 같은 볼륨을 목적지로 줘도 셸 규칙은 복사 그대로다.
        Assert.True(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out")!.Value.Copy);

        // Ctrl 은 셸 규칙에서 아무 뜻이 없다 — 여전히 복사다.
        Assert.True(
            DropRouter.Resolve(data, DragDropKeyStates.ControlKey, @"C:\outside")!.Value.Copy);
    }

    [Fact]
    public void ADropWithNeitherFormatIsNotResolved()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "그냥 글자");

        Assert.Null(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out"));
        Assert.Null(DropRouter.Resolve(null, DragDropKeyStates.None, @"C:\deck\out"));
    }

    /// <summary>사설 포맷 자리에 엉뚱한 것이 실려 있으면 받지 않는다 — 셸 분기로 흘러가지도 않는다.</summary>
    [Fact]
    public void APrivateFormatWithTheWrongPayloadIsNotResolved()
    {
        var data = new DataObject();
        data.SetData(FolderPanelView.FileDragFormat, "뭉치가 아니다");
        data.SetData(DataFormats.FileDrop, new[] { @"C:\outside\a.txt" });

        Assert.Equal(DropRoute.Internal, DropRouter.Route(data));
        Assert.Null(DropRouter.Resolve(data, DragDropKeyStates.None, @"C:\deck\out"));
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
