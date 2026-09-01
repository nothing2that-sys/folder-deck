namespace FolderDeck.App.Tests;

/// <summary>
/// 「지금 보고 있는 폴더」 메뉴가 부를 계층 (§4.6 · D75-b 6-3·6-4). 메뉴 XAML 배선은 다음
/// 회차(D75-c)다 — 여기서는 <see cref="FolderPanelViewModel.SetAnchorToCurrentCommand"/> ·
/// <see cref="FolderPanelViewModel.RegisterCurrentFolderCommand"/> 와 그 뒤의
/// <c>MainViewModel</c> 어댑터만 문다.
/// </summary>
public sealed class CurrentFolderMenuTests
{
    // ── 6-3. 경로 하나를 받는 앵커 지정 ──────────────────────────────────────

    // 8
    [Fact]
    public async Task GivenAPathTheEntryPathChangesAndSaves()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        // 하위로 들어가 "지금 보고 있는 폴더" 를 앵커 밖 위치로 만든다.
        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        await panel.SetAnchorToCurrentCommand.ExecuteAsync(null);

        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);

        // 저장이 실제로 돈다 — 디바운스를 밀어 파일을 다시 읽는다.
        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(expected, reloaded.Folders.Single(e => e.Id == id).Path);
    }

    /// <summary>어댑터 회귀 — 항목 우클릭 경로가 리팩터 뒤에도 그대로 돈다.</summary>
    // 9
    [Fact]
    public async Task TheExistingItemBasedCommandStillWorks()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var row = f.Row("작업");
        var id = row.Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        // f.Prompt.Answer 기본값이 true 라 표시 이름이 "Recipe" 로 바뀐다 — 그래서 미리 잡아 둔
        // row 참조로 확인한다("작업"으로 다시 찾으면 이름이 바뀐 뒤라 못 찾는다).
        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, row.Entry.Path);
        Assert.Equal(id, row.Entry.Id);
        Assert.Equal(expected, panel.CurrentPath);
    }

    /// <summary><c>Entry</c> 가 없으면 거절한다 — 지금처럼(B-0 §C.2).</summary>
    // 10
    [Fact]
    public async Task RejectsWhenThePanelHasNoEntry()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        // Entry 없이 CurrentPath 만 채운 상태를 억지로 만든다 — 가드가 무엇을 보는지 확인용.
        panel.CurrentPath = f.WorkPath;

        await panel.SetAnchorToCurrentCommand.ExecuteAsync(null);

        Assert.Null(panel.Entry);
        Assert.Equal(5, f.Workspace.Folders.Count);
    }

    // ── 6-4. 경로 하나를 받는 폴더 등록 ──────────────────────────────────────

    /// <summary>지금 보고 있는 폴더를 등록하면 <c>folders[]</c> 가 하나 는다.</summary>
    // 11
    [Fact]
    public async Task RegisteringTheCurrentFolderAddsOneToFolders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        panel.RegisterCurrentFolderCommand.Execute(null);

        var target = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(6, f.Workspace.Folders.Count);
        Assert.Contains(f.Workspace.Folders, e => e.Path == target);
    }
}
