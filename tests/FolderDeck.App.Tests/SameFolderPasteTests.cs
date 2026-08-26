using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>
/// 같은 폴더에 붙여넣기 = 제자리 복제 (Phase 8-D ② · 확인창은 8-E ②).
/// </summary>
/// <remarks>
/// 8-D 전에는 거절이었다 — 드롭 판정 ①(<see cref="MainViewModel.AcceptedForDrop"/>)이 온 곳이
/// 목적지와 같은 항목을 빼는데, 같은 폴더면 전부 빠져 받을 것이 0개가 됐다. 지금은 그 앞에서
/// 갈라져 §4.3 의 「둘 다 두기」로 간다.
/// <para>
/// <b>판정 단위는 뭉치다</b> — 클립보드의 <b>모든</b> 항목이 목적지에서 왔을 때만 이 길로 온다.
/// 일부만 같은 폴더인 혼합 뭉치는 8-D 전 동작 그대로다(아래 「혼합 뭉치」 묶음).
/// </para>
/// </remarks>
public sealed class SameFolderPasteTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static FolderPanelViewModel Tile(MainWindowFixture f, string path) =>
        f.ViewModel.Panels.Single(p => p.ShellDropDestination == path);

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    private static string[] NamesIn(string folder) =>
        [.. Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order()!];

    /// <summary>탐색기가 <c>Ctrl+X</c> 로 실어 놓는 모양 — <c>DROPEFFECT_MOVE</c>(2).</summary>
    private static DataObject Cut(params string[] paths)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream([2, 0, 0, 0]));
        return data;
    }

    // ── 파일 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>§4.3 충돌창은 뜨지 않는다</b> — 고를 것이 없다. 대신 8-E ② 가 진행/취소만 묻는
    /// 확인창을 하나 세웠다(아래 확인창 묶음).
    /// </summary>
    [Fact]
    public async Task PastingIntoTheSameFolderMakesACopyWithoutTheConflictDialog()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Main (2).cs", NamesIn(f.CodePath));
        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Single(f.Prompt.Messages);
    }

    /// <summary>
    /// 새 이름 형식은 <b>엔진이 쓰던 그것</b>이다 — 거듭 붙여넣으면 (2) 다음이 (3) 이다.
    /// </summary>
    [Fact]
    public async Task RepeatedPastesKeepCountingUp()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);
        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var names = NamesIn(f.CodePath);
        Assert.Contains("Main (2).cs", names);
        Assert.Contains("Main (3).cs", names);
    }

    /// <summary>원본은 그대로다 — 옆에 하나 더 둔 것이지 덮어쓴 것이 아니다.</summary>
    [Fact]
    public async Task TheOriginalIsUntouched()
    {
        using var f = await ReadyAsync();
        var original = Path.Combine(f.CodePath, "Main.cs");
        var before = File.ReadAllText(original);

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, File.ReadAllText(original));
        Assert.Equal(before, File.ReadAllText(Path.Combine(f.CodePath, "Main (2).cs")));
    }

    // ── 폴더 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 폴더도 제자리에 복제된다. 새 이름은 원본 <b>바깥</b>이라 재귀 복사가 자기 꼬리를 먹지 않는다.
    /// </summary>
    [Fact]
    public async Task AFolderIsDuplicatedInPlaceToo()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Recipe");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Recipe (2)", NamesIn(f.CodePath));

        // 안의 것까지 따라왔고, 복제본 안에 또 복제본이 생기지 않았다.
        Assert.Equal(["Recipe.cs"], NamesIn(Path.Combine(f.CodePath, "Recipe (2)")));
        Assert.Equal(["Recipe.cs"], NamesIn(Path.Combine(f.CodePath, "Recipe")));
    }

    // ── 저장된 정책은 여기 오지 않는다 ───────────────────────────────────────

    /// <summary>
    /// <b>「다음부터 묻지 않기」로 남긴 정책을 따르지 않는다.</b> 그 정책은 <i>다른</i> 폴더로
    /// 붙여넣을 때 겹치는 것을 어떻게 할지에 대한 답이고, 제자리 복제에는 덮어쓸 상대가 없다.
    /// </summary>
    [Theory]
    [InlineData(ConflictPolicy.Overwrite)]
    [InlineData(ConflictPolicy.Skip)]
    public async Task ARememberedPolicyDoesNotApplyHere(ConflictPolicy remembered)
    {
        using var f = await ReadyAsync();

        // 「다음부터 묻지 않기」가 남긴 모양 그대로 세운다.
        f.Workspace.OnConflict = remembered;
        f.Workspace.AskOnConflict = false;

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Main (2).cs", NamesIn(f.CodePath));
        Assert.Empty(f.Prompt.ConflictMessages);
    }

    /// <summary>정책을 새로 기억하지도 않는다 — 워크스페이스는 손대지 않는다.</summary>
    [Fact]
    public async Task TheWorkspacePolicyIsNotTouched()
    {
        using var f = await ReadyAsync();
        Assert.Null(f.Workspace.OnConflict);
        Assert.Null(f.Workspace.AskOnConflict);

        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Null(f.Workspace.OnConflict);
        Assert.Null(f.Workspace.AskOnConflict);
    }

    // ── 이동은 이 회차 범위가 아니다 ─────────────────────────────────────────

    /// <summary>
    /// 잘라내기로 같은 폴더에 붙여넣는 것은 <b>지금 그대로</b> 거절이다 — 같은 폴더로 옮기면서
    /// 이름만 바꾸는 것은 이동이 아니라 이름 바꾸기다(§4.6 이 따로 갖고 있다).
    /// </summary>
    [Fact]
    public async Task CuttingAndPastingIntoTheSameFolderStillDoesNothing()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        var before = NamesIn(f.CodePath);

        f.Clipboard.Data = Cut(Path.Combine(f.CodePath, "Main.cs"));

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, NamesIn(f.CodePath));
        Assert.Contains("붙여넣을 것이 없다", f.ViewModel.Message);
    }

    // ── 자기 안으로는 여전히 안 된다 ─────────────────────────────────────────

    /// <summary>
    /// ②(폴더를 자기 자신이나 자기 하위로)는 제자리 복제도 예외가 아니다 — 목적지를 삼키는
    /// 폴더가 섞여 있으면 뭉치째 거절이다.
    /// </summary>
    [Fact]
    public async Task PastingAFolderIntoItselfIsStillRejected()
    {
        using var f = await ReadyAsync();
        var recipe = Path.Combine(f.CodePath, "Recipe");

        var code = Tile(f, f.CodePath);
        await code.NavigateToCommand.ExecuteAsync(recipe);

        // 「Recipe」 자신을 클립보드에 싣고, Recipe 안을 보고 있는 타일에 붙여넣는다.
        f.Clipboard.Data = ClipboardService.FileDataObject([new OperationItem(recipe, true)]);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(["Recipe.cs"], NamesIn(recipe));
        Assert.Contains("붙여넣을 것이 없다", f.ViewModel.Message);
    }

    // ── 혼합 뭉치 — 지금 동작 그대로 ─────────────────────────────────────────

    /// <summary>
    /// <b>일부만 같은 폴더면 제자리 복제로 보지 않는다.</b> 판정이 뭉치 단위라서다 — 정책은
    /// 조작 하나에 하나이고(<c>FileOperationRequest.OnConflict</c>), 항목마다 갈라 붙이면 한 번
    /// 누른 붙여넣기가 두 번의 조작·두 개의 보고가 된다.
    /// <para>
    /// 그래서 혼합은 8-D 전과 같다: 같은 폴더에서 온 항목은 ① 이 빼고(사본이 생기지 않는다)
    /// 나머지만 들어온다.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AMixedBatchFallsBackToTheOldRule()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);

        // code/Main.cs (목적지와 같은 폴더) + code/Recipe/Recipe.cs (하위 폴더)
        f.Clipboard.Data = ClipboardService.FileDataObject([
            new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false),
            new OperationItem(Path.Combine(f.CodePath, "Recipe", "Recipe.cs"), false),
        ]);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var names = NamesIn(f.CodePath);

        // 하위에서 온 것만 들어왔다.
        Assert.Contains("Recipe.cs", names);

        // 같은 폴더에서 온 것은 사본이 생기지 않았다 — ① 이 빼 갔다.
        Assert.DoesNotContain("Main (2).cs", names);
    }

    /// <summary>혼합에서 빠진 건수는 결과에 적힌다 — 조용히 사라지지 않는다 (§4.9 · §5).</summary>
    [Fact]
    public async Task TheSkippedCountIsReportedInAMixedBatch()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);

        f.Clipboard.Data = ClipboardService.FileDataObject([
            new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false),
            new OperationItem(Path.Combine(f.CodePath, "Recipe", "Recipe.cs"), false),
        ]);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("같은 폴더", f.ViewModel.Message);
    }

    // ── 확인창 (Phase 8-E ②) ─────────────────────────────────────────────────
    //
    // §4.3 충돌창이 아니다. 저쪽은 셋 중 하나를 고르는 창이고 여기는 고를 것이 없다 —
    // 결과가 하나뿐이라 미리 보여 주고 진행/취소만 받는다.

    /// <summary>창이 <b>무엇이 어떤 이름으로 생기는지</b>를 적는다.</summary>
    [Fact]
    public async Task TheConfirmationSaysWhatNameWillAppear()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("Main.cs → Main (2).cs", message);
        Assert.Contains("원본은 그대로 남습니다", message);
    }

    /// <summary>
    /// 창이 적은 이름은 <b>엔진이 실제로 짓는 이름</b>이다 — 형식을 화면 쪽에 다시 쓰지 않았다.
    /// 이미 (2) 가 있으면 창도 (3) 이라고 적고 실제로도 (3) 이 생긴다.
    /// </summary>
    [Fact]
    public async Task ThePreviewAgreesWithWhatActuallyGetsCreated()
    {
        using var f = await ReadyAsync();
        File.WriteAllText(Path.Combine(f.CodePath, "Main (2).cs"), "이미 있다");

        var code = Tile(f, f.CodePath);
        await code.RefreshAsync();
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("Main.cs → Main (3).cs", Assert.Single(f.Prompt.Messages));
        Assert.Contains("Main (3).cs", NamesIn(f.CodePath));
    }

    /// <summary><b>취소하면 아무것도 생기지 않는다.</b></summary>
    [Fact]
    public async Task CancellingCreatesNothing()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        var before = NamesIn(f.CodePath);

        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        f.Prompt.Answer = false;
        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, NamesIn(f.CodePath));
        Assert.Contains("취소", f.ViewModel.Message);
    }

    /// <summary>여럿이면 줄마다 하나씩 적는다.</summary>
    [Fact]
    public async Task EveryItemGetsItsOwnLineWhenThereAreFew()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs", "Recipe");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("사본 2개를 만듭니다", message);
        Assert.Contains("Main.cs → Main (2).cs", message);
        Assert.Contains("Recipe → Recipe (2)", message);
        Assert.DoesNotContain("외 ", message);
    }

    /// <summary>
    /// 많으면 앞 다섯 줄만 적고 나머지는 "외 N개"로 접는다 — 전부 적으면 창이 화면을 넘어가
    /// <b>버튼이 안 보인다</b>. 접혀도 <b>실제로는 전부</b> 복제된다.
    /// </summary>
    [Fact]
    public async Task ManyItemsFoldIntoACountButAllOfThemAreStillCopied()
    {
        using var f = await ReadyAsync();
        var folder = Path.Combine(f.CodePath, "many");
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 8; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"f{i}.txt"), "x");
        }

        var code = Tile(f, f.CodePath);
        await code.NavigateToCommand.ExecuteAsync(folder);
        code.SelectedItems = [.. code.Items];
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        var message = Assert.Single(f.Prompt.Messages);
        Assert.Contains("사본 8개를 만듭니다", message);
        Assert.Contains("외 3개", message);

        // 다섯 줄만 보이지만 여덟 개가 다 생긴다.
        Assert.Equal(16, Directory.GetFiles(folder).Length);
    }

    /// <summary>
    /// 확인창은 <b>제자리 복제에만</b> 붙는다 — 다른 폴더로 붙여넣는 길은 §4.3 그대로다.
    /// </summary>
    [Fact]
    public async Task PastingIntoAnotherFolderDoesNotGetThisConfirmation()
    {
        using var f = await ReadyAsync();
        var output = Tile(f, f.OutputPath);

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await output.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.Messages);
        Assert.Contains("Main.cs", NamesIn(f.OutputPath));
    }
}
