using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>매크로 — 저장된 규칙 (§4.7 · Phase 3 Stage 5).</summary>
public sealed class MacroTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    /// <summary>창이 제안을 그대로 받아들이되 필요한 것만 바꾸게 한다.</summary>
    private static void Accept(MainWindowFixture f, Func<MacroDraft, MacroDraft>? tweak = null) =>
        f.MacroEditor.Transform = d => tweak is null ? d : tweak(d);

    // ── 만들기 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoMacroWithoutASelection()
    {
        using var f = await ReadyAsync();

        Assert.True(f.ViewModel.MacrosAreEmpty);
        Assert.False(f.ViewModel.CanCreateMacro);

        f.ViewModel.CreateMacro();

        Assert.Equal(0, f.MacroEditor.CallCount);
        Assert.True(f.ViewModel.MacrosAreEmpty);
    }

    /// <summary>
    /// 창은 <b>지금 화면 상태로 미리 채워져</b> 뜬다 — 방금 손으로 한 조작을 굳히는 것이 주 용도다.
    /// </summary>
    [Fact]
    public async Task TheEditorOpensPrefilledFromTheCurrentState()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Accept(f);

        f.ViewModel.CreateMacro();

        var suggested = f.MacroEditor.Suggested!;
        Assert.Equal(FileOperationKind.Copy, suggested.Op);
        Assert.Equal(MacroSourceKind.Selection, suggested.SourceKind);
        Assert.Equal(MacroDestKind.Tray, suggested.DestKind);
        Assert.True(suggested.Confirm);
        Assert.Contains("문서", suggested.Name);

        // 대상이 하나라 "이 경로로 고정"을 고를 수 있다.
        Assert.True(f.MacroEditor.CanFixPath);
        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), suggested.FixedPath);
    }

    /// <summary>대상이 여럿이면 경로 하나로 굳힐 수 없다 — §4.7 의 source.path 는 경로 하나다.</summary>
    [Fact]
    public async Task ManySourcesCannotBePinnedToOnePath()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs", "Recipe");
        Accept(f);

        f.ViewModel.CreateMacro();

        Assert.False(f.MacroEditor.CanFixPath);
        Assert.Null(f.MacroEditor.Suggested!.FixedPath);
    }

    [Fact]
    public async Task CancelingTheEditorCreatesNothing()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Answer = null;

        f.ViewModel.CreateMacro();

        Assert.True(f.ViewModel.MacrosAreEmpty);
        Assert.Null(f.Workspace.Macros);
    }

    [Fact]
    public async Task AMacroPersistsAndComesBackAfterARestart()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Accept(f, d => d with { Name = "문서로 복사", OnConflict = ConflictPolicy.Skip });

        f.ViewModel.CreateMacro();

        var card = Assert.Single(f.ViewModel.Macros);
        Assert.Equal("문서로 복사", card.Name);
        Assert.Contains("대상함", card.Summary);
        Assert.Contains("건너뛰기", card.Summary);

        f.Store.Flush();
        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var restarted = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt,
            f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        var reloaded = Assert.Single(restarted.Macros);
        Assert.Equal("문서로 복사", reloaded.Name);
        Assert.Equal(ConflictPolicy.Skip, reloaded.Definition.OnConflict);
        Assert.Equal(MacroSourceKind.Selection, reloaded.Definition.Source!.Kind);
    }

    /// <summary>move·trash 는 확인을 끄지 못한다 (§4.7).</summary>
    [Fact]
    public async Task ConfirmCannotBeTurnedOffForMoveOrTrash()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        Accept(f, d => d with { Op = FileOperationKind.Move, Confirm = false });

        f.ViewModel.CreateMacro();

        Assert.True(f.ViewModel.Macros[0].Definition.Confirm);
    }

    [Fact]
    public async Task RemovingAMacroClearsTheSlotWhenItWasTheLast()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        Accept(f);
        f.ViewModel.CreateMacro();

        f.ViewModel.RemoveMacro(f.ViewModel.Macros[0]);

        Assert.True(f.ViewModel.MacrosAreEmpty);

        // 빈 배열을 파일에 남기지 않는다.
        f.Store.Flush();
        Assert.Null(f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow().Macros);
    }

    // ── 고치기 ───────────────────────────────────────────────────────────────

    /// <summary>고치기는 만들기와 달리 지금 고른 것이 없어도 된다.</summary>
    [Fact]
    public async Task EditDoesNotRequireASelection()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.Panels[0].ClearSelection();

        Assert.True(f.ViewModel.CanEditMacro);

        Accept(f, d => d with { Name = "새 이름" });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal("새 이름", f.ViewModel.Macros[0].Name);
    }

    /// <summary>지우고 다시 만드는 게 아니라 같은 규칙을 갱신한다.</summary>
    [Fact]
    public async Task EditingUpdatesTheExistingRuleInPlace()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        var original = f.ViewModel.Macros[0];

        Accept(f, d => d with { Name = "고친 이름", OnConflict = ConflictPolicy.Skip });
        f.ViewModel.EditMacro(original);

        var card = Assert.Single(f.ViewModel.Macros);
        Assert.Same(original, card);
        Assert.Equal("고친 이름", card.Name);
        Assert.Equal(ConflictPolicy.Skip, card.Definition.OnConflict);
    }

    [Fact]
    public async Task CancelingAnEditLeavesTheRuleUnchanged()
    {
        using var f = await WithMacroAsync(d => d with { Name = "원래 이름" }, "Main.cs");
        f.MacroEditor.Answer = null;

        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal("원래 이름", f.ViewModel.Macros[0].Name);
    }

    /// <summary>창은 지우고 다시 만드는 게 아니라 있는 규칙의 값 그대로 채워져 뜬다.</summary>
    [Fact]
    public async Task EditingPassesTheExistingValuesAsTheSuggestedDraft()
    {
        using var f = await WithMacroAsync(
            d => d with { Name = "문서로 복사", OnConflict = ConflictPolicy.Skip }, "Main.cs");
        Accept(f);

        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        var suggested = f.MacroEditor.Suggested!;
        Assert.Equal("문서로 복사", suggested.Name);
        Assert.Equal(ConflictPolicy.Skip, suggested.OnConflict);
        Assert.True(f.MacroEditor.WasEdit);
    }

    /// <summary>
    /// dest 축을 안 건드린 수정(예: 이름만)이 "지금 체크된 폴더로 고정"을 조용히 다시
    /// 굳히면 안 된다 — 편집 시점의 대상함 체크가 만들 때와 달라도 원래 굳힌 폴더 그대로다.
    /// </summary>
    [Fact]
    public async Task EditingWithoutTouchingDestKeepsThePinnedFolders()
    {
        var f = await ReadyAsync();
        using var _ = f;

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Transform = d => d with { DestKind = MacroDestKind.FolderIds };
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;

        // 체크를 바꿔 지금 체크된 것과 굳혀 둔 것이 달라지게 한다.
        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));

        // dest 축은 안 건드리고 이름만 고친다.
        Accept(f, d => d with { Name = "이름만 고침" });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        // 원래 굳힌 "문서"로 가야 한다 — 지금 체크된 "산출물"로 새면 안 된다.
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    /// <summary>고치는 도중 새로 "지금 체크된 폴더로 고정"을 고르면 그 순간 체크를 굳힌다.</summary>
    [Fact]
    public async Task SwitchingToPinnedFoldersDuringEditFreezesWhatIsCheckedNow()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        Accept(f, d => d with { DestKind = MacroDestKind.FolderIds });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        // 이후 체크를 바꿔도 고칠 때 굳힌 "문서"를 그대로 써야 한다.
        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    /// <summary>이미 고정 경로였던 규칙은 지금 선택이 없어도 그 값 그대로 고칠 수 있다.</summary>
    [Fact]
    public async Task EditingAFixedPathRuleWorksWithNothingCurrentlySelected()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");
        f.ViewModel.Panels[0].ClearSelection();

        Accept(f, d => d with { Name = "고정 규칙 고침" });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.True(f.MacroEditor.CanFixPath);
        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), f.MacroEditor.Suggested!.FixedPath);
        Assert.Equal("고정 규칙 고침", f.ViewModel.Macros[0].Name);
    }

    /// <summary>
    /// "지금 고른 파일로 다시 고정"을 켜면 실제로 고정 경로가 바뀐다(코덱스 적대적 검증
    /// 발견 — 예전에는 이 체크박스 자체가 없어서 재지정할 방법이 아예 없었다).
    /// </summary>
    [Fact]
    public async Task RetargetingAFixedPathRuleDuringEditPointsItAtTheNewSelection()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");

        // 만들 때와 다른 것을 하나 고르고, 다시 고정을 명시적으로 켠다.
        Select(f.ViewModel.Panels[0], "Recipe");
        Accept(f, d => d with { RetargetFixedPath = true });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal(
            Path.Combine(f.CodePath, "Recipe"),
            f.ViewModel.Macros[0].Definition.Source!.Path);
    }

    /// <summary>체크박스를 안 켜면 다른 걸 고르고 있어도 예전 고정 경로를 그대로 지킨다.</summary>
    [Fact]
    public async Task NotRetargetingAFixedPathRuleKeepsTheOldPathEvenWithADifferentSelection()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");
        var originalPath = f.ViewModel.Macros[0].Definition.Source!.Path;

        Select(f.ViewModel.Panels[0], "Recipe");
        Accept(f); // RetargetFixedPath 는 기본값 false

        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Assert.Equal(originalPath, f.ViewModel.Macros[0].Definition.Source!.Path);
    }

    /// <summary>
    /// "지금 체크된 폴더로 다시 고정"을 켜면 실제로 목적지가 바뀐다(코덱스 적대적 검증
    /// 발견 — 예전에는 이 체크박스 자체가 없어서 굳힌 폴더 목록을 갱신할 방법이 없었다).
    /// </summary>
    [Fact]
    public async Task RepinningFolderIdsRuleDuringEditUpdatesTheDestinations()
    {
        var f = await ReadyAsync();
        using var _ = f;

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Transform = d => d with { DestKind = MacroDestKind.FolderIds };
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));

        Accept(f, d => d with { RepinFolderIds = true });
        f.ViewModel.EditMacro(f.ViewModel.Macros[0]);

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        // 다시 고정을 켰으니 이제는 새로 체크된 "산출물"로 가야 한다.
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    // ── 실행: 대상 풀기 ──────────────────────────────────────────────────────

    private static async Task<MainWindowFixture> WithMacroAsync(
        Func<MacroDraft, MacroDraft> tweak, params string[] selection)
    {
        var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], selection);
        f.MacroEditor.Transform = d => tweak(d);
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;
        return f;
    }

    [Fact]
    public async Task SelectionSourceUsesWhateverIsSelectedWhenItRuns()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        // 만들 때와 <b>다른</b> 것을 고르고 돌린다.
        Select(f.ViewModel.Panels[1], "a.dll");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "a.dll")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    [Fact]
    public async Task SelectionSourceRefusesWhenNothingIsSelected()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.ViewModel.Panels[0].ClearSelection();

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("대상이 없다", f.ViewModel.Message);
        Assert.Empty(f.Prompt.Messages);
    }

    /// <summary>"특정 경로 파일을 열려 있는 모든 폴더로 복사" — §4.7 의 대표 예다.</summary>
    [Fact]
    public async Task AFixedPathGoesToEveryVisibleFolder()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath, DestKind = MacroDestKind.AllVisible },
            "Main.cs");

        // 순환 칸에도 폴더를 하나 띄워 "보이는 폴더"를 늘린다.
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        f.ViewModel.Panels[0].ClearSelection();

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        // code(자기 자신) · 산출물 · 작업 — 자기 자신은 "원본과 목적지가 같다"로 건너뛴다.
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    [Fact]
    public async Task AFixedPathThatVanishedSaysSoInsteadOfRunning()
    {
        using var f = await WithMacroAsync(
            d => d with { SourceKind = MacroSourceKind.FixedPath }, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        File.Delete(Path.Combine(f.CodePath, "Main.cs"));

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("고정 경로가 사라졌다", f.ViewModel.Message);
        Assert.Empty(f.Prompt.Messages);
    }

    // ── 실행: 목적지 풀기 ────────────────────────────────────────────────────

    /// <summary>"지금 체크된 폴더로 고정"은 만드는 순간에 굳는다 — 나중에 체크가 바뀌어도 그대로다.</summary>
    [Fact]
    public async Task PinnedFolderIdsIgnoreLaterTrayChanges()
    {
        var f = await ReadyAsync();
        using var _ = f;

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.MacroEditor.Transform = d => d with { DestKind = MacroDestKind.FolderIds };
        f.ViewModel.CreateMacro();
        f.MacroEditor.Transform = null;

        // 체크를 바꾸고 다른 폴더를 넣어도 규칙은 문서를 가리킨다.
        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.AddToTray(f.Row("산출물"));

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    [Fact]
    public async Task TrayDestinationFollowsTheChecksAtRunTime()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");

        // 만들 때는 대상함이 비어 있었다. 실행할 때 체크된 것을 쓴다.
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    [Fact]
    public async Task NoDestinationSaysSoInsteadOfRunning()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("목적지가 없다", f.ViewModel.Message);
    }

    /// <summary>이동은 목적지가 하나여야 한다 (§4.2) — 규칙이라고 예외가 아니다.</summary>
    [Fact]
    public async Task AMoveMacroRefusesManyDestinations()
    {
        using var f = await WithMacroAsync(d => d with { Op = FileOperationKind.Move }, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.ViewModel.AddToTray(f.Row("산출물"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("목적지가 2개", f.ViewModel.Message);
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
    }

    // ── 안전장치 ─────────────────────────────────────────────────────────────

    /// <summary>실행 전 미리보기가 기본이다 — "무엇 → 어디, N건, 목적지별 내역" (§4.7).</summary>
    [Fact]
    public async Task ThePreviewSpellsOutWhatWillHappen()
    {
        using var f = await WithMacroAsync(d => d, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.Prompt.Answer = false;

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        var shown = Assert.Single(f.Prompt.Messages);
        Assert.Contains("1개 → 폴더 1개 = 1건", shown);
        Assert.Contains(f.DocsPath, shown);

        // 취소했으므로 아무 일도 없다.
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("취소", f.ViewModel.Message);
    }

    [Fact]
    public async Task ThePreviewCountsCollisionsAndSaysWhatWillBeDoneWithThem()
    {
        using var f = await WithMacroAsync(
            d => d with { OnConflict = ConflictPolicy.Skip }, "Main.cs");
        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.Prompt.Answer = false;

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("같은 이름 1건은 건너뜁니다", Assert.Single(f.Prompt.Messages));
    }

    /// <summary>확인을 끈 복사 규칙은 창 없이 바로 돈다 — 그것이 자동화의 뜻이다.</summary>
    [Fact]
    public async Task AConfirmFreeCopyRunsWithoutAnyDialog()
    {
        using var f = await WithMacroAsync(d => d with { Confirm = false }, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.True(f.ViewModel.Macros[0].RunsWithoutConfirm);
    }

    /// <summary>
    /// 매크로는 <b>자기 정책</b>을 쓴다 — 워크스페이스의 "겹치면 묻기"(§4.3)는 직접 조작용이다.
    /// 반복 실행이 존재 이유인데 매번 물으면 자동화가 아니다.
    /// </summary>
    [Fact]
    public async Task AMacroNeverOpensTheConflictDialog()
    {
        using var f = await WithMacroAsync(
            d => d with { Confirm = false, OnConflict = ConflictPolicy.Rename }, "Main.cs");
        File.WriteAllText(Path.Combine(f.DocsPath, "Main.cs"), "옛 내용");
        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main (2).cs")));
    }

    [Fact]
    public async Task ATrashMacroNeedsNoDestinationAndAlwaysConfirms()
    {
        using var f = await WithMacroAsync(
            d => d with { Op = FileOperationKind.Trash, Confirm = false }, "Main.cs");
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.Prompt.Answer = true;

        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Contains("휴지통으로 보냅니다", Assert.Single(f.Prompt.Messages));
        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), Assert.Single(f.RecycleBin.Sent));
        Assert.Contains("휴지통", f.ViewModel.Macros[0].Summary);
    }

    [Fact]
    public async Task ListsRefreshAfterAMacroRuns()
    {
        using var f = await WithMacroAsync(d => d with { Confirm = false }, "Main.cs");
        var docsTile = f.ViewModel.Tiles.Single(t => t.IsRotating);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(docsTile.Panel.Items);

        f.ViewModel.AddToTray(f.Row("문서"));
        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.RunMacroAsync(f.ViewModel.Macros[0]);

        Assert.Equal(2, docsTile.Panel.Items.Count);
    }
}
