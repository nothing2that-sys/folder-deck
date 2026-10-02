using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

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

    private static void Collide(MainWindowFixture f, string name = "Main.cs") =>
        File.WriteAllText(Path.Combine(f.DocsPath, name), "옛 내용");

    private static async Task ReadyToCopyAsync(MainWindowFixture f)
    {
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        await Task.CompletedTask;
    }

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

        Assert.DoesNotContain("원본은 사라집니다", asked);
    }

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

        Select(f.ViewModel.Panels[0], "Main.cs");
        await f.ViewModel.CopySelectionAsync();

        Assert.Single(f.Prompt.ConflictMessages);
        Assert.Equal("옛 내용", File.ReadAllText(Path.Combine(f.DocsPath, "Main.cs")));

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(ConflictPolicy.Skip, reloaded.OnConflict);
        Assert.False(reloaded.AskOnConflict);
    }

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

        Assert.Equal(ConflictPolicy.Skip, f.ViewModel.ConflictPolicy);
    }

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
        Assert.Empty(f.Prompt.Messages);
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

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

    [Fact]
    public async Task TrashNeverAsks()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[1], "a.dll");

        await f.ViewModel.TrashSelectionAsync();

        Assert.Empty(f.Prompt.ConflictMessages);
    }
}
