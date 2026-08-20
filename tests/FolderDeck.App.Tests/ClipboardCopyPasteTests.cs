using System.Windows;
using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.App.Views;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

/// <summary>
/// 복사(<c>Ctrl+C</c>)와 붙여넣기(<c>Ctrl+V</c>) — §4.6 310행 · 330~336행 (A군 ③).
/// </summary>
/// <remarks>
/// 여기서 시험하는 것은 <b>무엇이 실리고, 무엇을 거절하고, 무엇이 실제로 복사되는가</b>다.
/// 진짜 클립보드는 STA 스레드를 요구해 부를 수 없으므로(<see cref="FakeClipboardService"/>)
/// "클립보드에 올리는 줄"과 "탐색기가 그것을 어떻게 푸는가"는 손 확인으로 남는다 —
/// <see cref="ShellExportTests"/> · <see cref="ShellDropTests"/> 와 같은 경계다.
/// </remarks>
public sealed class ClipboardCopyPasteTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    // ── C-1 복사 ─────────────────────────────────────────────────────────────

    /// <summary>대상은 「휴지통으로 삭제」와 같이 <b>고른 것 전부</b>다. 폴더도 간다.</summary>
    [Fact]
    public async Task CopyPutsEveryPickedPathOnTheClipboard()
    {
        using var f = await ReadyAsync();
        var panel = f.ViewModel.Panels[0];
        Select(panel, "Recipe", "Main.cs");

        panel.CopySelectionToClipboardCommand.Execute(null);

        var picked = Assert.Single(f.Clipboard.FileSets);

        Assert.Equal(
            [Path.Combine(f.CodePath, "Main.cs"), Path.Combine(f.CodePath, "Recipe")],
            picked.Select(i => i.Path).Order());

        Assert.True(picked.Single(i => i.Path.EndsWith("Recipe")).IsDirectory);
    }

    /// <summary>
    /// 실리는 것은 셸 파일 포맷 + <c>Preferred DropEffect</c>(복사)다 — 내보내기(§4.9)와 같은 것.
    /// </summary>
    /// <remarks>
    /// 클립보드를 건드리지 않는 <see cref="ClipboardService.FileDataObject"/> 를 직접 부른다 —
    /// <b>가짜가 아니라 실물 코드</b>가 무엇을 싣는지 보는 자리다.
    /// </remarks>
    [Fact]
    public void WhatCopyPutsOnTheClipboardCarriesTheShellFormatAndSaysCopy()
    {
        var data = ClipboardService.FileDataObject([
            new OperationItem(@"C:\deck\work\Main.cs", false),
            new OperationItem(@"C:\deck\work\Views", true),
        ]);

        Assert.Equal(
            [@"C:\deck\work\Main.cs", @"C:\deck\work\Views"],
            Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop)));

        var effect = Assert.IsType<MemoryStream>(data.GetData(ShellExport.PreferredDropEffectFormat));

        Assert.Equal([1, 0, 0, 0], effect.ToArray());
    }

    /// <summary>고른 것이 없으면 거절이고 <b>클립보드를 건드리지 않는다</b>.</summary>
    /// <remarks>빈 것으로 덮으면 아까 복사해 둔 것이 사라진다 — 그래서 "아무 일 없음"이 아니다.</remarks>
    [Fact]
    public async Task CopyingNothingIsRejectedAndLeavesTheClipboardAlone()
    {
        using var f = await ReadyAsync();

        f.ViewModel.Panels[0].CopySelectionToClipboardCommand.Execute(null);

        Assert.Empty(f.Clipboard.FileSets);
        Assert.Null(f.Clipboard.Data);
        Assert.Contains("복사할 것이 없다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);

        // 거절이라 스스로 사라진다 (D28) — CreateTile 이 거절 통로를 걸어 주었다는 뜻이기도 하다.
        Assert.True(f.ViewModel.MessageIsTransient);
    }

    /// <summary>패널만 떼어 만든다 — 통로 둘을 갈라 받는 자리라 상위가 없어도 된다.</summary>
    private static FolderPanelViewModel Bare(
        Action<string>? reportError, Action<string>? reportRejection) =>
        new(
            new FolderEnumerator(),
            new FakeShellLauncher(),
            new FakeClipboardService(),
            isRotating: false,
            reportError,
            reportRejection);

    /// <summary>거절은 <b>거절 통로</b>로 간다 — 실패 통로는 조용하다 (D28).</summary>
    [Fact]
    public void CopyingNothingGoesDownTheRejectionChannelNotTheFailureOne()
    {
        var failures = new List<string>();
        var rejections = new List<string>();
        var panel = Bare(failures.Add, rejections.Add);

        panel.CopySelectionToClipboard();

        Assert.Contains("복사할 것이 없다", Assert.Single(rejections));
        Assert.Empty(failures);
    }

    /// <summary>
    /// 거절 통로를 안 받았으면 <b>실패 통로로 떨어진다</b> — 새 생성 자리가 인자를 잊어도
    /// 거절이 조용히 사라지지 않는다 (D28).
    /// </summary>
    [Fact]
    public void WithoutARejectionChannelTheRejectionFallsBackToTheFailureOne()
    {
        var failures = new List<string>();
        var panel = Bare(failures.Add, reportRejection: null);

        panel.CopySelectionToClipboard();

        Assert.Contains("복사할 것이 없다", Assert.Single(failures));
    }

    /// <summary>클립보드 쓰기 실패는 조용히 삼키지 않는다 (§5) — 서비스가 준 메시지를 그대로.</summary>
    [Fact]
    public async Task AFailedClipboardWriteIsToldToTheUser()
    {
        using var f = await ReadyAsync();
        var panel = f.ViewModel.Panels[0];
        Select(panel, "Main.cs");
        f.Clipboard.Error = "클립보드가 잠겨 있다";

        panel.CopySelectionToClipboardCommand.Execute(null);

        Assert.Contains("클립보드에 올리지 못했다", f.ViewModel.Message);
        Assert.Contains("클립보드가 잠겨 있다", f.ViewModel.Message);
    }

    /// <summary>
    /// 「경로 복사」는 그대로 <b>글자</b>를 싣는다 — 새 커맨드가 그 자리를 가져가지 않았다.
    /// </summary>
    [Fact]
    public async Task CopyPathStillPutsTextOnTheClipboardAndIsADifferentCommand()
    {
        using var f = await ReadyAsync();
        var panel = f.ViewModel.Panels[0];

        panel.CopyItemPathCommand.Execute(panel.Items.Single(i => i.Name == "Main.cs"));

        Assert.Equal(Path.Combine(f.CodePath, "Main.cs"), f.Clipboard.Text);
        Assert.Empty(f.Clipboard.FileSets);
    }

    // ── C-2 붙여넣기 ─────────────────────────────────────────────────────────

    /// <summary>이 경로를 보고 있는 타일. 상시 칸의 순서를 짐작하지 않는다.</summary>
    private static FolderPanelViewModel Tile(MainWindowFixture f, string path) =>
        f.ViewModel.Panels.Single(p => p.ShellDropDestination == path);

    /// <summary>탐색기가 <c>Ctrl+X</c> 로 실어 놓는 모양 — <c>DROPEFFECT_MOVE</c>(2).</summary>
    private static DataObject Cut(params string[] paths)
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths, autoConvert: true);
        data.SetData(
            ShellExport.PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes(2u)));
        return data;
    }

    /// <summary>
    /// 클립보드에서 꺼낸 것을 푸는 것은 <b>셸 드롭이 쓰는 그 파서</b>다 — 새 파서를 만들지 않았다.
    /// </summary>
    [Fact]
    public async Task WhatComesOffTheClipboardIsParsedByTheExistingShellParser()
    {
        using var f = await ReadyAsync();
        var panel = Tile(f, f.CodePath);
        Select(panel, "Recipe", "Main.cs");
        panel.CopySelectionToClipboardCommand.Execute(null);

        var items = DropRouter.ShellItems(f.Clipboard.GetData()!);

        Assert.Equal(2, items.Count);
        Assert.True(items.Single(i => i.Path.EndsWith("Recipe")).IsDirectory);
        Assert.False(items.Single(i => i.Path.EndsWith("Main.cs")).IsDirectory);
    }

    /// <summary>목적지는 앵커가 아니라 타일이 <b>지금 보고 있는 폴더</b>다 (§4.9 442행).</summary>
    [Fact]
    public async Task PasteCopiesTheClipboardFilesIntoTheFolderTheTileIsShowing()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.Rotating.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        var views = Path.Combine(f.WorkPath, "Views");
        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await f.Rotating.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(views, "Main.cs")));

        // 붙여넣기는 <b>복사</b>다 — 원본이 남는다.
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
        Assert.Empty(f.Prompt.Messages);   // 복사는 확인창을 지나지 않는다 (D35)
    }

    /// <summary>결과는 기존 보고 경로를 탄다 — 낱말만 「붙여넣기」다 (§5).</summary>
    [Fact]
    public async Task PasteReportsThroughTheExistingResultPath()
    {
        using var f = await ReadyAsync();
        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("붙여넣기 — 성공 1 · 건너뜀 0 · 실패 0", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
    }

    /// <summary>
    /// 잘라내기는 <b>이동</b>으로 붙여넣는다 (D35) — 목적지에 생기고 <b>원본이 사라진다</b>.
    /// </summary>
    [Fact]
    public async Task PasteMovesWhenTheClipboardSaysCutAndRemovesTheOriginal()
    {
        using var f = await ReadyAsync();
        f.Clipboard.Data = Cut(Path.Combine(f.CodePath, "Main.cs"));

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
        Assert.Contains("붙여넣기 — 성공 1 · 건너뜀 0 · 실패 0", f.ViewModel.Message);
        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.Empty(f.Prompt.Messages);   // 이동 붙여넣기도 확인창을 지나지 않는다 (D35)
    }

    /// <summary>빈 클립보드는 거절이다 — 파일 수가 그대로다.</summary>
    [Fact]
    public async Task PasteWithAnEmptyClipboardIsRejected()
    {
        using var f = await ReadyAsync();
        var before = Directory.GetFiles(f.OutputPath).Length;

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Equal(before, Directory.GetFiles(f.OutputPath).Length);
        Assert.Contains("붙여넣을 파일이 없다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
    }

    /// <summary>아직 아무 폴더도 안 띄운 순환 칸은 붙여넣을 자리가 아니다.</summary>
    [Fact]
    public async Task PasteIsRejectedWhenTheTileHasNoDestination()
    {
        using var f = new MainWindowFixture();
        Assert.Null(f.Rotating.ShellDropDestination);

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await f.Rotating.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("붙여넣을 곳이 없다", f.ViewModel.Message);
        Assert.True(f.ViewModel.MessageIsWarning);
    }

    /// <summary>
    /// 접근 불가 폴더를 보고 있는 타일도 마찬가지다 — 목적지 속성이 이미 <c>null</c> 로 거른다.
    /// </summary>
    [Fact]
    public async Task PasteIsRejectedOnATileShowingAnUnreachableFolder()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await f.Rotating.PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Contains("붙여넣을 곳이 없다", f.ViewModel.Message);
    }

    /// <summary>
    /// <b>온 곳과 같은 폴더</b>에 붙여넣으면 거절이다 — 드롭 판정(①)이 이미 그렇게 답한다.
    /// </summary>
    /// <remarks>
    /// 새 규칙이 아니다: <see cref="MainViewModel.AcceptedForDrop"/> 가 항목마다 부모를 보고
    /// 목적지와 같은 것을 빼는데, 같은 폴더면 전부 빠져 0개가 되고 받을 것이 없어진다.
    /// </remarks>
    [Fact]
    public async Task PastingBackIntoTheFolderTheItemsCameFromIsRejected()
    {
        using var f = await ReadyAsync();
        var code = Tile(f, f.CodePath);
        Select(code, "Main.cs");
        code.CopySelectionToClipboardCommand.Execute(null);

        await code.PasteIntoCurrentCommand.ExecuteAsync(null);

        // "Main - 복사본.cs" 같은 것이 생기지 않는다.
        Assert.Equal(["Main.cs"], Directory.GetFiles(f.CodePath).Select(Path.GetFileName).Order());
        Assert.Contains("붙여넣을 것이 없다", f.ViewModel.Message);
    }

    /// <summary>겹치면 충돌창이 뜬다 (§4.3 · D21 — <c>StartOperationAsync</c> 에 얹은 값이다).</summary>
    /// <remarks>
    /// <b>창 본문은 「복사」라고 말한다</b> — 그 문구는 <c>op</c> 로 만들어지고 「붙여넣기」라는
    /// 낱말은 진행 줄과 결과 요약에만 들어간다. 여기서 못박는 것은 <b>창이 떴다</b>는 것이다.
    /// </remarks>
    [Fact]
    public async Task PasteAsksWhenNamesCollide()
    {
        using var f = await ReadyAsync();
        File.WriteAllText(Path.Combine(f.OutputPath, "Main.cs"), "old");

        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Single(f.Prompt.ConflictMessages);
        Assert.Contains("같은 이름으로 이미 있습니다", f.Prompt.ConflictMessages[0]);
        Assert.Equal(ConflictPolicy.Overwrite, f.Prompt.SuggestedPolicy);
    }

    /// <summary>안 겹치면 창이 뜨지 않는다 — 즉흥적인 붙여넣기가 매번 막히지 않는다.</summary>
    [Fact]
    public async Task PasteDoesNotAskWhenNothingCollides()
    {
        using var f = await ReadyAsync();
        f.Clipboard.Data = ClipboardService.FileDataObject(
            [new OperationItem(Path.Combine(f.CodePath, "Main.cs"), false)]);

        await Tile(f, f.OutputPath).PasteIntoCurrentCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    /// <summary>모든 타일에 붙여넣기 통로가 걸려 있다 (D23 — 여덟 번째 칸).</summary>
    [Fact]
    public void EveryTilePanelHasThePasteChannelWired()
    {
        using var f = new MainWindowFixture();

        Assert.All(f.ViewModel.Panels, p => Assert.NotNull(p.PasteInto));
    }

    /// <summary>
    /// 생성자 인자 수는 그대로다 — 붙여넣기가 새 의존을 들이지 않았다는 못이다(§8 정지 조건 2).
    /// </summary>
    [Fact]
    public void TheMainViewModelConstructorStillTakesTenArguments()
    {
        var constructor = Assert.Single(typeof(MainViewModel).GetConstructors());

        Assert.Equal(10, constructor.GetParameters().Length);
    }
}
