using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 우클릭 메뉴 「이 폴더를 앵커로 지정」 (§4.6 305행 · A군 ① D8~D11). <b>쓰기다</b> —
/// <c>folders[]</c> 의 <c>path</c> 가 바뀌고 저장 경로를 탄다.
/// </summary>
public sealed class SetAnchorTests
{
    /// <summary>밀린 디바운스 저장을 마무리시키고 저장 파일을 다시 읽는다.</summary>
    /// <remarks>
    /// <b>기다리지 않고 <c>IWorkspaceStore.Flush</c> 로 민다</b> — 디바운스 타이머 콜백은
    /// 스레드 풀에서 도는데, 전체 스위트를 병렬로 돌리는 첫(콜드) 회차에는 그 콜백이 초 단위로
    /// 밀린다. 폴링으로 기다리게 짰더니 5초를 기다리고도 옛 스냅샷을 읽는 회차가 나왔다.
    /// <c>Flush</c> 는 밀린 쓰기를 <b>이 스레드에서 동기로</b> 수행하므로 그 타이밍이 사라진다.
    /// <para>
    /// 그래도 짧게 확인은 한다 — 타이머가 이미 깨어나 쓰기 중이면 <c>Flush</c> 가 밀 것을
    /// 못 찾고 지나가고, 그 원자적 교체가 끝나기 전을 읽을 수 있다.
    /// </para>
    /// </remarks>
    private static async Task<Workspace> ReloadAsync(
        MainWindowFixture f, Guid id, Func<Workspace, bool> ready)
    {
        f.Store.Flush();

        Workspace? last = null;

        for (var i = 0; i < 40; i++)
        {
            if (f.Store.LoadWorkspace(id).Value is { } workspace)
            {
                last = workspace;
                if (ready(workspace))
                {
                    return workspace;
                }
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException(
            last is null ? "저장 파일이 안 나타났다" : "저장 파일이 바뀐 값을 안 담았다");
    }

    [Fact]
    public async Task ReplacesThePathAndKeepsTheFolderId()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var row = f.Row("작업");
        var id = row.Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, row.Entry.Path);

        // folderId 는 그대로다 — 새로 만들면 대상함·매크로·타일의 참조가 고아가 된다.
        Assert.Equal(id, row.Entry.Id);

        // 등록 수는 안 늘어난다 — 이것은 등록이 아니라 교체다.
        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Equal(5, f.Workspace.Folders.Count);
    }

    /// <summary>저장되는 <c>folders[]</c> 와 화면 쪽 목록이 같이 간다.</summary>
    [Fact]
    public async Task BothLayersMove()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Views"));

        var expected = Path.Combine(f.WorkPath, "Views");
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, f.ViewModel.Rows.Single(r => r.Entry.Id == id).PathText);
    }

    /// <summary>앵커가 바뀌면 그 타일은 새 앵커를 보고 있다 (§4.6 292행).</summary>
    [Fact]
    public async Task TheTileLandsOnTheNewAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);
        Assert.Contains(panel.DisplayItems, i => i.Name == "Recipe.cs");
    }

    /// <summary><c>currentPath</c> 는 앵커가 바뀌어도 저장 파일에 새어 들지 않는다 (§4.6 292행).</summary>
    [Fact]
    public async Task CurrentPathNeverReachesTheFile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");

        // 새 앵커를 벗어나 둔다 — currentPath 와 path 가 확실히 갈리게.
        await panel.GoUpAsync();
        Assert.True(panel.IsAwayFromAnchor);

        await ReloadAsync(f, f.Workspace.Id, w => w.Folders.Any(e => e.Path == expected));
        var json = await File.ReadAllTextAsync(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("currentPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recipe", json);
    }

    /// <summary>앱을 다시 열면 새 앵커가 그대로다 — 저장을 탔다는 증거.</summary>
    [Fact]
    public async Task SurvivesAReload()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");
        var reloaded = await ReloadAsync(
            f, f.Workspace.Id, w => w.Folders.Single(e => e.Id == id).Path == expected);

        var entry = reloaded.Folders.Single(e => e.Id == id);
        Assert.Equal(expected, entry.Path);
        Assert.Equal(id, entry.Id);

        // 등록 수는 그대로 — 교체이지 등록이 아니다.
        Assert.Equal(5, reloaded.Folders.Count);
    }

    // ── D10 확인창 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task YesRenamesTheDisplayNameToTheLeaf()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var entry = f.Workspace.Folders.Single(e => e.Id == id);
        Assert.Equal("Recipe", entry.DisplayName);
        Assert.Equal("Recipe", panel.AnchorName);

        // 확인창은 딱 한 번 떴다.
        Assert.Single(f.Prompt.Messages);
    }

    [Fact]
    public async Task NoKeepsTheOldDisplayName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = false;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var entry = f.Workspace.Folders.Single(e => e.Id == id);
        Assert.Equal("작업", entry.DisplayName);

        // "아니오"는 이름만 막는다 — 앵커는 옮겨졌다.
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), entry.Path);
    }

    /// <summary><c>description</c> 은 편집 대상이 아니다 (D10).</summary>
    [Fact]
    public async Task DescriptionIsNeverTouched()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal("탐색용", f.Workspace.Folders.Single(e => e.Id == id).Description);
        Assert.Empty(f.FolderEditor.Opened);   // B군의 수정 대화상자는 끌어오지 않았다
    }

    // ── D9 중복 거절 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectsAPathAnotherEntryAlreadyHas()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        // work 의 부모로 올라가면 목록에 code·output·docs 가 형제로 보인다.
        await panel.GoUpAsync();
        var before = f.Row("작업").Entry.Path;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "code"));

        Assert.Equal(before, f.Row("작업").Entry.Path);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("이미 등록된 폴더다", f.ViewModel.Message);

        // 거절이면 확인창도 안 뜨고 타일도 안 움직인다.
        Assert.Empty(f.Prompt.Messages);
        Assert.NotEqual(f.CodePath, panel.CurrentPath);
    }

    /// <summary>제자리 지정은 할 일이 없다 — 거절하고 이유를 말한다.</summary>
    [Fact]
    public async Task RejectsTheAnchorItself()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.GoUpAsync();

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
        Assert.Contains("이미 앵커다", f.ViewModel.Message);
        Assert.Empty(f.Prompt.Messages);
    }

    // ── 문턱 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FilesAreIgnored()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
        Assert.Empty(f.Prompt.Messages);
    }

    [Fact]
    public async Task NullAndEmptyTileAreIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(null);

        Assert.Null(panel.Entry);
        Assert.Empty(f.Prompt.Messages);
    }

    /// <summary>상시 칸에서도 같은 메뉴가 그 타일의 앵커를 옮긴다 (D8).</summary>
    [Fact]
    public async Task WorksOnAPinnedTileToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var pinned = f.ViewModel.Tiles
            .Single(t => t.IsPinned && t.Panel.Entry is not null && t.Panel.Entry.Path == f.CodePath)
            .Panel;

        var id = pinned.Entry!.Id;

        await pinned.SetAnchorToItemCommand.ExecuteAsync(
            pinned.DisplayItems.Single(i => i.Name == "Views"));

        var expected = Path.Combine(f.CodePath, "Views");
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, pinned.CurrentPath);

        // 다른 타일의 앵커는 건드리지 않았다.
        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
    }
}
