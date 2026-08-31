using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

/// <summary>
/// 그리드 파일을 왼쪽 폴더 행 / 대상함으로 끌어다 놓기 (§4.2-2 · Phase 3 Stage 3).
/// </summary>
/// <remarks>
/// 실제 마우스 제스처(<c>DoDragDrop</c>)는 뷰의 몫이라 여기서 흉내 내지 않는다. 뷰가 만드는
/// 것은 <see cref="FileDropPayload"/> 하나이고, 받는 쪽 판정과 실행이 전부 뷰모델에 있으므로
/// 그 경계를 그대로 시험한다 — 규칙(같은 폴더 거부·자기 안 거부)이 뷰에 새어 나가지 않게.
/// </remarks>
public sealed class DragDropTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    // ── 뭉치 만들기 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ThereIsNothingToDragWithoutASelection()
    {
        using var f = await ReadyAsync();

        Assert.Null(f.ViewModel.Panels[0].BuildDragPayload());
    }

    [Fact]
    public async Task ThePayloadCarriesTheSelectionAndTheFolderItCameFrom()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");

        var payload = code.BuildDragPayload()!;

        Assert.Equal(f.CodePath, payload.SourceFolder);
        Assert.Equal(2, payload.Items.Count);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Main.cs") && !i.IsDirectory);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Recipe") && i.IsDirectory);
    }

    // ── 누른 항목 (D72 — 임계를 넘기 전에 선택이 바뀌어도 누른 파일이 끌린다) ──────

    /// <summary>같은 확장자·다른 자리의 항목을 견주는 시험 몇 개가 대소문자만 다른 경로를
    /// 만들 때 쓴다 — 실제 파일이 아니라 순전히 <see cref="FileItemViewModel.FullPath"/> 값만
    /// 다른 인스턴스를 흉내 낸다.</summary>
    private static FileItemViewModel FakeItem(string fullPath, bool isDirectory = false) =>
        new(new FolderItem(Path.GetFileName(fullPath), fullPath, isDirectory, Size: 0, ModifiedUtc: DateTime.UtcNow));

    [Fact]
    public async Task PressingAnItemInsideTheSelectionCarriesTheWholeSelection()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");
        var pressed = code.Items.Single(i => i.Name == "Main.cs");

        var payload = code.BuildDragPayload(pressed)!;

        Assert.Equal(2, payload.Items.Count);
    }

    /// <summary>
    /// 누른 항목이 선택 밖이면 그 하나만 실린다. <b>대소문자만 다른 경로로도 한 번 더
    /// 확인한다</b>(§5-3 2번) — 5-1 이 요구한 <c>FullPath</c> 비교 방식(대소문자 무시)이
    /// 실제로 걸렸는지 무는 자리다. 대소문자 비교가 잘못 걸려 "다른 파일"로 보면, 원래는
    /// 선택 안이어야 할 것이 여기(선택 밖 → 하나만)로 새어 들어온다 — 그래서 이 판이 함께
    /// 막는다.
    /// </summary>
    [Fact]
    public async Task PressingAnItemOutsideTheSelectionCarriesOnlyThatItem()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs");
        var pressed = code.Items.Single(i => i.Name == "Recipe");

        var payload = code.BuildDragPayload(pressed)!;

        Assert.Equal(f.CodePath, payload.SourceFolder);
        Assert.Single(payload.Items);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Recipe") && i.IsDirectory);

        // 대소문자 갈래 — 선택을 둘로 넓히고, 그중 하나와 경로만 대소문자가 다른 것을
        // 누른다. 대소문자를 가려 비교했다면 "선택 밖"으로 보여 위 판과 똑같이 1개만
        // 실렸을 것이다 — 여기서는 "선택 안"으로 보고 둘 다 실려야 5-1 의 비교 방식이
        // 맞다는 것이 증명된다.
        Select(code, "Main.cs", "Recipe");
        var mainCs = code.Items.Single(i => i.Name == "Main.cs");
        var pressedDifferentCase = FakeItem(mainCs.FullPath.ToUpperInvariant());

        var casePayload = code.BuildDragPayload(pressedDifferentCase)!;

        Assert.Equal(2, casePayload.Items.Count);
    }

    [Fact]
    public async Task NoPressedItemMeansTheWholeSelectionAsBefore()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");

        var payload = code.BuildDragPayload()!;

        Assert.Equal(2, payload.Items.Count);
    }

    /// <summary>아무것도 안 골라진 채 파일 하나를 눌러 곧장 끌어도 그 하나가 실린다 —
    /// D70 Q2 가 확인한 "드래그가 아예 시작되지 않는" 증상이 이 자리다.</summary>
    [Fact]
    public async Task PressingAnItemWithNoSelectionAtAllStillCarriesThatOneItem()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        var pressed = code.Items.Single(i => i.Name == "Main.cs");

        var payload = code.BuildDragPayload(pressed)!;

        Assert.NotNull(payload);
        Assert.Single(payload.Items);
        Assert.Contains(payload.Items, i => i.Path == Path.Combine(f.CodePath, "Main.cs") && !i.IsDirectory);
    }

    [Fact]
    public async Task NoCurrentFolderMeansNoPayloadEvenWithAPressedItem()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        var pressed = code.Items.Single(i => i.Name == "Main.cs");
        code.Clear();

        Assert.Null(code.BuildDragPayload(pressed));
    }

    // ── 기본 이동 · Ctrl 복사 ────────────────────────────────────────────────

    [Fact]
    public async Task DroppingMovesByDefault()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));

        // 드래그 이동은 확인창을 거치지 않는다 — 손으로 과녁을 겨눈 한 번의 동작이다.
        Assert.Empty(f.Prompt.Messages);
        Assert.Contains("성공 1", f.ViewModel.Message);
    }

    [Fact]
    public async Task DroppingWithControlCopies()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: true);

        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    [Fact]
    public async Task ManyItemsGoInOneDrop()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll", "b.dll");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.ViewModel.Panels[1].BuildDragPayload()!, copy: false);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "a.dll")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "b.dll")));
        Assert.Empty(Directory.GetFiles(f.OutputPath));
    }

    [Fact]
    public async Task AFolderIsDroppedWholeAndRecursively()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Recipe");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs")));
        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
    }

    // ── 받지 않는 자리 (과녁을 켜지 않는다) ─────────────────────────────────

    /// <summary>같은 폴더 안으로 놓기 = 무동작. 원본을 건드리지 않는다.</summary>
    [Fact]
    public async Task DroppingIntoTheFolderItCameFromDoesNothing()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        var payload = f.Rotating.BuildDragPayload()!;

        Assert.False(f.ViewModel.CanDropOnto(f.Row("작업").Entry, payload));

        await f.ViewModel.DropOntoFolderAsync(f.Row("작업").Entry, payload, copy: false);

        // 아무 일도 없었다 — 파일도, 알림도.
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.Null(f.ViewModel.Message);
    }

    [Fact]
    public async Task AFolderCannotBeDroppedOntoItself()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Recipe");
        var payload = f.Rotating.BuildDragPayload()!;

        // 대상함/리스트에 그 폴더 자체가 등록돼 있는 경우.
        var recipe = new Core.Models.FolderEntry { Path = Path.Combine(f.WorkPath, "Recipe") };
        var inside = new Core.Models.FolderEntry
        {
            Path = Path.Combine(f.WorkPath, "Recipe", "Controls"),
        };

        Assert.False(f.ViewModel.CanDropOnto(recipe, payload));
        Assert.False(f.ViewModel.CanDropOnto(inside, payload));
    }

    [Fact]
    public async Task AnEmptyPayloadIsNotAccepted()
    {
        using var f = await ReadyAsync();

        Assert.False(f.ViewModel.CanDropOnto(f.Row("문서").Entry, null));
        Assert.False(f.ViewModel.CanDropOnto(f.Row("문서").Entry, new FileDropPayload(f.WorkPath, [])));
        Assert.False(f.ViewModel.CanDropOnto(null, new FileDropPayload(f.WorkPath, [])));
    }

    // ── 대상함으로 놓기 ──────────────────────────────────────────────────────

    /// <summary>
    /// 대상함에 놓으면 <b>그 항목 하나</b>로만 간다 — 체크된 목적지 집합(버튼 실행)과 섞이지 않는다.
    /// </summary>
    [Fact]
    public async Task DroppingOnATrayEntryTargetsThatEntryAlone()
    {
        using var f = await ReadyAsync();
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        var docs = f.ViewModel.Tray.Single(t => t.DisplayName == "문서");
        await f.ViewModel.DropOntoFolderAsync(docs.Entry, f.Rotating.BuildDragPayload()!, copy: true);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    // ── 실패 · 갱신 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 닿을 수 없는 목적지에 놓으면 시작하기 전에 막고 이유가 상태 바에 남는다 (§5).
    /// </summary>
    /// <remarks>
    /// 미리보기가 살아 있는 목적지를 하나도 못 찾으면 엔진을 부르지 않는다 — 어차피 전부
    /// "건너뜀"으로 돌아올 것을 실행해 보고 알려 주는 것보다 낫다.
    /// </remarks>
    [Fact]
    public async Task DroppingOnAnUnreachableFolderIsRefusedWithAReasonThatStays()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("설비 로그").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.Contains("목적지에 닿지 못했다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);

        // 이동이었지만 원본은 그대로다 — 목적지에 닿지 못했으니 손대지 않았다.
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    [Fact]
    public async Task ListsRefreshAfterADrop()
    {
        using var f = await ReadyAsync();
        var docsTile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(docsTile.Panel.Items);

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.ViewModel.Panels[0].BuildDragPayload()!, copy: true);

        Assert.Equal(2, docsTile.Panel.Items.Count);
    }
}
