using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 동일 이름 충돌 — 겹칠 때만 묻고, 고른 것을 기억한다 (§4.3 · Phase 3 Stage 4).
/// </summary>
public sealed class ConflictPolicyTests
{
    private static async Task<MainWindowFixture> ReadyAsync()
    {
        var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        return f;
    }

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    /// <summary>문서 폴더에 같은 이름을 심어 충돌을 만든다.</summary>
    private static void Collide(MainWindowFixture f, string name = "Main.cs") =>
        File.WriteAllText(Path.Combine(f.DocsPath, name), "옛 내용");

    private static async Task ReadyToCopyAsync(MainWindowFixture f)
    {
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        await Task.CompletedTask;
    }

    // ── 언제 묻는가 ──────────────────────────────────────────────────────────

    /// <summary>안 겹치면 창이 뜨지 않는다 — 대부분의 복사는 그대로 지나가야 한다.</summary>
    [Fact]
    public async Task NothingIsAskedWhenNothingCollides()
    {
        using var f = await ReadyAsync();
        await ReadyToCopyAsync(f);

        await f.ViewModel.CopySelectionAsync();

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Contains("성공 1", f.ViewModel.Message);
    }

    [Fact]
    public async Task ACollisionAsksAndSaysHowManyAndWhere()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);

        await f.ViewModel.CopySelectionAsync();

        var asked = Assert.Single(f.Prompt.ConflictMessages);
        Assert.Contains("1건이 목적지에 같은 이름으로 이미 있습니다", asked);

        // 복사는 원본이 남으므로 "원본은 사라집니다"가 들어가면 거짓말이다.
        Assert.DoesNotContain("원본은 사라집니다", asked);
    }

    /// <summary>기본 단추는 지금 정책에 맞춘다 — 손이 기억하는 대로 눌러도 같은 결과.</summary>
    [Fact]
    public async Task TheDialogIsToldWhichPolicyIsCurrent()
    {
        using var f = await ReadyAsync();
        f.Workspace.OnConflict = ConflictPolicy.Rename;
        Collide(f);
        await ReadyToCopyAsync(f);

        await f.ViewModel.CopySelectionAsync();

        Assert.Equal(ConflictPolicy.Rename, f.Prompt.SuggestedPolicy);
    }

    // ── 세 선택지가 실제로 다르게 동작하는가 ────────────────────────────────

    [Fact]
    public async Task OverwriteReplacesTheExistingFile()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Overwrite, Remember: false);

        await f.ViewModel.CopySelectionAsync();

        Assert.Equal(300, new FileInfo(Path.Combine(f.DocsPath, "Main.cs")).Length);
        Assert.Contains("덮어씀 1", f.ViewModel.Message);
    }

    [Fact]
    public async Task SkipLeavesTheExistingFileUntouched()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: false);

        await f.ViewModel.CopySelectionAsync();

        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("건너뜀 1", f.ViewModel.Message);
    }

    [Fact]
    public async Task RenameKeepsBoth()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Rename, Remember: false);

        await f.ViewModel.CopySelectionAsync();

        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main (2).cs")));
        Assert.Contains("성공 1", f.ViewModel.Message);
    }

    [Fact]
    public async Task CancelingTheDialogTouchesNothing()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = null;

        await f.ViewModel.CopySelectionAsync();

        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("취소", f.ViewModel.Message);
        Assert.False(f.ViewModel.IsOperationRunning);
    }

    // ── 기억하기 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RememberingStopsTheAskingAndPersists()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: true);

        await f.ViewModel.CopySelectionAsync();

        Assert.False(f.ViewModel.AskOnConflict);
        Assert.Equal(ConflictPolicy.Skip, f.ViewModel.ConflictPolicy);

        // 두 번째 복사는 묻지 않고 건너뛴다.
        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.CopySelectionAsync();

        Assert.Single(f.Prompt.ConflictMessages);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));

        // 재시작해도 남아 있다.
        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(ConflictPolicy.Skip, reloaded.OnConflict);
        Assert.False(reloaded.AskOnConflict);
    }

    /// <summary>기본값(덮어쓰기)을 기억해도 파일에 슬롯을 남기지 않는다.</summary>
    [Fact]
    public async Task RememberingOverwriteKeepsTheFileClean()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Overwrite, Remember: true);

        await f.ViewModel.CopySelectionAsync();

        Assert.Null(f.Workspace.OnConflict);
        Assert.Equal(ConflictPolicy.Overwrite, f.ViewModel.ConflictPolicy);
        Assert.False(f.ViewModel.AskOnConflict);
    }

    [Fact]
    public async Task TheTrayLineSaysWhatWillHappenAndOffersAWayBack()
    {
        using var f = await ReadyAsync();

        Assert.Contains("물어본다", f.ViewModel.ConflictPolicyText);
        Assert.False(f.ViewModel.CanRestoreConflictPrompt);

        Collide(f);
        await ReadyToCopyAsync(f);
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: true);
        await f.ViewModel.CopySelectionAsync();

        Assert.Contains("건너뛴다", f.ViewModel.ConflictPolicyText);
        Assert.Contains("묻지 않음", f.ViewModel.ConflictPolicyText);
        Assert.True(f.ViewModel.CanRestoreConflictPrompt);

        f.ViewModel.RestoreConflictPrompt();

        Assert.True(f.ViewModel.AskOnConflict);
        Assert.False(f.ViewModel.CanRestoreConflictPrompt);
        Assert.Contains("물어본다", f.ViewModel.ConflictPolicyText);

        // 정책 자체는 남는다 — 다시 물을 때 기본 단추로 쓰인다.
        Assert.Equal(ConflictPolicy.Skip, f.ViewModel.ConflictPolicy);
    }

    // ── 이동 · 드래그도 같은 규칙 ────────────────────────────────────────────

    /// <summary>
    /// 이동이고 겹치면 <b>창은 하나만</b> 뜬다 — 확인창과 충돌창이 잇달아 뜨면
    /// 두 번 읽고 두 번 눌러야 한다. 그래서 충돌창이 "원본은 사라집니다"까지 말한다.
    /// </summary>
    [Fact]
    public async Task AMoveWithCollisionsAsksOnlyOnce()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.MoveSelectionAsync();

        Assert.Contains("원본은 사라집니다", Assert.Single(f.Prompt.ConflictMessages));
        Assert.Empty(f.Prompt.Messages);   // 별도의 이동 확인창은 뜨지 않는다
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    /// <summary>안 겹치는 이동은 예전처럼 확인창만 뜬다.</summary>
    [Fact]
    public async Task AMoveWithoutCollisionsStillUsesThePlainConfirm()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.MoveSelectionAsync();

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Contains("원본은 사라진다", Assert.Single(f.Prompt.Messages));
    }

    /// <summary>묻지 않기로 해 뒀으면 이동 확인창이 무엇을 할지 알려 준다.</summary>
    [Fact]
    public async Task TheMoveConfirmSpellsOutTheSilentPolicy()
    {
        using var f = await ReadyAsync();
        f.Workspace.OnConflict = ConflictPolicy.Skip;
        f.Workspace.AskOnConflict = false;
        Collide(f);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.MoveSelectionAsync();

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Contains("같은 이름 1건은 건너뛴다", Assert.Single(f.Prompt.Messages));
    }

    [Fact]
    public async Task DraggingOntoACollisionAsksToo()
    {
        using var f = await ReadyAsync();
        Collide(f);
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: false);

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: true);

        Assert.Single(f.Prompt.ConflictMessages);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));
    }

    /// <summary>안 겹치는 드롭은 여전히 창 없이 지나간다 — 드래그의 존재 이유다.</summary>
    [Fact]
    public async Task ADragWithoutCollisionsStaysSilent()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");

        await f.ViewModel.DropOntoFolderAsync(
            f.Row("문서").Entry, f.Rotating.BuildDragPayload()!, copy: false);

        Assert.Empty(f.Prompt.ConflictMessages);
        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
    }

    /// <summary>폴더를 합칠 때도 안쪽 파일 충돌을 세어 묻는다.</summary>
    [Fact]
    public async Task MergingAFolderCountsAsACollision()
    {
        using var f = await ReadyAsync();
        Directory.CreateDirectory(Path.Combine(f.DocsPath, "Recipe"));
        File.WriteAllText(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs"), "옛 레시피");

        Select(f.ViewModel.Panels[0], "Recipe");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.Prompt.ConflictAnswer = new ConflictDecision(ConflictPolicy.Skip, Remember: false);

        await f.ViewModel.CopySelectionAsync();

        Assert.Single(f.Prompt.ConflictMessages);
        Assert.Equal("옛 레시피", File.ReadAllText(Path.Combine(f.DocsPath, "Recipe", "Recipe.cs")));
    }

    /// <summary>휴지통은 목적지가 없어 충돌이 없다 — 묻지 않는다.</summary>
    [Fact]
    public async Task TrashNeverAsks()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Empty(f.Prompt.ConflictMessages);
    }
}
