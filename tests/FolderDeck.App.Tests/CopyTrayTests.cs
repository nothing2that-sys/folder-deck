using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Operations;

namespace FolderDeck.App.Tests;

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

    [Fact]
    public async Task ARefreshKeepsTheSelection()
    {
        using var f = await ReadyAsync();
        var code = f.ViewModel.Panels[0];
        Select(code, "Main.cs", "Recipe");

        await f.ViewModel.RefreshAllAsync();

        Assert.True(f.ViewModel.HasSelection);
        Assert.Equal(["Main.cs", "Recipe"], code.SelectedItems.Select(i => i.Name).Order());

        Assert.All(code.SelectedItems, i => Assert.Contains(i, code.Items));
    }

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
        Assert.True(File.Exists(Path.Combine(f.CodePath, "Main.cs")));
        Assert.Contains("성공 2", f.ViewModel.Message);
    }

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

    [Fact]
    public async Task PartialFailureIsSummarisedAndDetailed()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("문서"));
        f.ViewModel.AddToTray(f.Row("설비 로그"));

        await f.ViewModel.CopySelectionAsync();

        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.Contains("성공 1", f.ViewModel.Message);
        Assert.Contains("건너뜀 1", f.ViewModel.Message);

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

        Assert.True(row.Entry.Pinned);

        f.ViewModel.ToggleLayoutEdit();
        f.ViewModel.RemoveTile(f.ViewModel.Tiles.Single(t => t.Spec.FolderId == row.Entry.Id));
        Assert.False(row.Pinned);
        Assert.False(row.Entry.Pinned);
    }

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

    [Fact]
    public async Task AlreadyPinnedFoldersGoIntoTheTrayToo()
    {
        using var f = await ReadyAsync();

        f.ViewModel.AddToTray(f.Row("code"));

        Assert.Equal("code", Assert.Single(f.ViewModel.Tray).DisplayName);
    }

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

        Assert.Empty(f.Prompt.Messages);
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));
        Assert.False(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
    }

    [Fact]
    public async Task OneDestinationMovesAndManyDestinationsStillCopy()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Select(f.Rotating, "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        await f.ViewModel.CopySelectionAsync();
        Assert.True(File.Exists(Path.Combine(f.OutputPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.DocsPath, "Main.cs")));

        f.ViewModel.Tray[0].IsChecked = false;
        f.ViewModel.NotifyOperationState();
        Assert.DoesNotContain("이동은 목적지 하나만", f.ViewModel.OperationSummary);

        await f.ViewModel.MoveSelectionAsync();
        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
    }

    [Fact]
    public async Task TheSummaryWarnsAboutMoveBeforeYouPressIt()
    {
        using var f = await ReadyAsync();
        Select(f.ViewModel.Panels[0], "Main.cs");
        f.ViewModel.AddToTray(f.Row("산출물"));
        f.ViewModel.AddToTray(f.Row("문서"));

        Assert.Contains("이동은 목적지 하나만", f.ViewModel.OperationSummary);
    }

    [Fact]
    public async Task OverlappingRefreshesDoNotThrow()
    {
        using var f = await ReadyAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        var storm = Enumerable.Range(0, 8).Select(_ => f.ViewModel.RefreshAllAsync()).ToList();

        await Task.WhenAll(storm);

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
