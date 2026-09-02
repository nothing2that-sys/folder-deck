using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 우클릭 메뉴 「이름 바꾸기」 / <c>F2</c> (§4.6 309행 · A군 ② B-1). <b>실제 파일 시스템이 바뀐다</b> —
/// 열기·속성은 읽기였고 등록·앵커 지정은 JSON 만 고쳤다.
/// </summary>
/// <remarks>
/// 앵커 자신을 바꾸는 경우(D12~D15)는 <see cref="RenameAnchorTests"/> 가 본다.
/// </remarks>
public sealed class RenameTests
{
    // ── 성공 경로 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenamesAFolderOnDisk()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.False(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe2")));

        // 안에 있던 것이 따라왔다 — 지우고 만든 것이 아니라 옮긴 것이다.
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Recipe2", "Recipe.cs")));
    }

    [Fact]
    public async Task RenamesAFileOnDisk()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Program.cs";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.False(File.Exists(Path.Combine(f.WorkPath, "Main.cs")));
        Assert.True(File.Exists(Path.Combine(f.WorkPath, "Program.cs")));
    }

    /// <summary>성공 뒤 목록이 새 이름으로 보인다 — 명시적 재조회의 관찰점이다.</summary>
    [Fact]
    public async Task TheListShowsTheNewName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Contains(panel.DisplayItems, i => i.Name == "Recipe2");
        Assert.DoesNotContain(panel.DisplayItems, i => i.Name == "Recipe");
    }

    /// <summary>바꾼 것이 골라진 채로 남는다 — 옛 경로로는 되찾을 수 없는 자리다(§4.8).</summary>
    [Fact]
    public async Task TheRenamedItemStaysSelected()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var selected = Assert.Single(panel.SelectedItems);
        Assert.Equal("Recipe2", selected.Name);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe2"), selected.FullPath);
    }

    [Fact]
    public async Task SaysWhatItRenamed()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.Contains("Recipe", f.ViewModel.Message);
        Assert.Contains("Recipe2", f.ViewModel.Message);
    }

    /// <summary>창에는 지금 이름이 미리 채워져 들어간다.</summary>
    [Fact]
    public async Task TheDialogIsPrefilledWithTheCurrentName()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.Equal("Main.cs", Assert.Single(f.Prompt.NamesAsked));
    }

    /// <summary>
    /// 모달의 확인 버튼 문구는 그대로 「바꾸기」다(회귀, §4.6 · D75-e) — 새 폴더 만들기가
    /// 같은 모달을 「만들기」로 재사용하게 되면서, 이름 바꾸기 쪽이 실수로 따라 바뀌지
    /// 않는지를 본다.
    /// </summary>
    [Fact]
    public async Task AcceptButtonTextIsStillChange()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null; // 취소해도 뜬 순간에 넘어간 문구만 본다
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal("바꾸기", Assert.Single(f.Prompt.AcceptButtonTextsAsked));
    }

    /// <summary>대소문자만 바꾸는 것은 같은 파일이라 "이미 있다"로 막지 않는다.</summary>
    [Fact]
    public async Task ChangingOnlyTheCaseIsAllowed()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "RECIPE";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.Equal(
            "RECIPE",
            Path.GetFileName(Directory.GetDirectories(f.WorkPath).Single(d => d.EndsWith("ECIPE"))));
    }

    // ── 취소 ─────────────────────────────────────────────────────────────────

    /// <summary>취소는 아무것도 바꾸지 않고 상태 바에도 적지 않는다.</summary>
    [Fact]
    public async Task CancelChangesNothing()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = null;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        f.ViewModel.Message = null;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.Null(f.ViewModel.Message);
    }

    /// <summary>같은 이름을 다시 적는 것은 할 일이 없다 — 창은 떴지만 옮기지 않는다.</summary>
    [Fact]
    public async Task TheSameNameIsANoOp()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        f.ViewModel.Message = null;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.Null(f.ViewModel.Message);
    }

    // ── 거절 넷 (§4-4) — 전부 상태 바에 이유가 남는다 ─────────────────────────

    [Fact]
    public async Task RejectsANameThatAlreadyExists()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Views";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("같은 이름이 이미 있다", f.ViewModel.Message);
    }

    /// <remarks>
    /// <b>금칙 문자·예약 이름의 행렬은 <see cref="FolderDeck.Core.Tests"/> 쪽 규칙 테스트가 본다</b> —
    /// 그쪽은 픽스처가 필요 없다. 여기서는 범주마다 한 건씩만 두어 <b>거절이 상태 바까지
    /// 올라오고 디스크가 그대로인지</b>를 본다. 같은 행렬을 여기서 또 돌리면 픽스처가 수십 개
    /// 더 서고, 그 병렬 I/O 부하가 다른 테스트의 디바운스 저장을 밀어낸다(실측).
    /// </remarks>
    [Fact]
    public async Task RejectsAnEmptyName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "   ";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("빈 이름", f.ViewModel.Message);
    }

    [Fact]
    public async Task RejectsForbiddenCharacters()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = @"a\b";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("쓸 수 없는 문자", f.ViewModel.Message);
    }

    [Fact]
    public async Task RejectsWindowsReservedNames()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Aux.txt";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("예약 이름", f.ViewModel.Message);
    }

    /// <summary>끝의 점은 Windows 가 말없이 깎는다 — 조용히 다듬지 않고 거절한다.</summary>
    [Fact]
    public async Task RejectsATrailingDot()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe.";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe")));
        Assert.Contains("점이나 공백", f.ViewModel.Message);
    }

    /// <summary>
    /// 파일 시스템 예외는 삼키지 않는다 — 잠긴 파일을 열어 둔 채로 바꿔 본다.
    /// </summary>
    [Fact]
    public async Task SurfacesAFileSystemFailure()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Locked.cs";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var target = Path.Combine(f.WorkPath, "Main.cs");
        using (var hold = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await panel.RenameItemCommand.ExecuteAsync(
                panel.DisplayItems.Single(i => i.Name == "Main.cs"));
        }

        Assert.True(File.Exists(target));
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("이름을 바꾸지 못했다", f.ViewModel.Message);

        // 실패는 스스로 지워지지 않는다 — 화면이 이유를 보여 주지 않기 때문이다.
        Assert.False(f.ViewModel.MessageIsTransient);
    }

    // ── F2 진입점 (D16) — 메뉴와 같은 경로를 부른다 ────────────────────────────

    [Fact]
    public async Task F2RenamesTheSingleSelectedItem()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SelectedItems = [panel.DisplayItems.Single(i => i.Name == "Recipe")];
        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.True(Directory.Exists(Path.Combine(f.WorkPath, "Recipe2")));
        Assert.Equal("Recipe", Assert.Single(f.Prompt.NamesAsked));
    }

    [Fact]
    public async Task F2WithNothingSelectedIsSilent()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        f.ViewModel.Message = null;

        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.NamesAsked);
        Assert.Null(f.ViewModel.Message);
    }

    /// <summary>여럿 고른 상태는 거절한다 — 어느 것을 말하는지 정할 근거가 없다.</summary>
    [Fact]
    public async Task F2WithManySelectedIsRejected()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.SelectedItems = [.. panel.DisplayItems.Take(2)];
        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.NamesAsked);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("하나만", f.ViewModel.Message);

        // 거절이라 스스로 사라진다 (D28) — 위의 "이름을 바꾸지 못했다"(실패)와 갈리는 자리다.
        Assert.True(f.ViewModel.MessageIsTransient);
    }

    // ── 문턱 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NullAndEmptyTileAreIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(null);
        await panel.RenameSelectionCommand.ExecuteAsync(null);

        Assert.Empty(f.Prompt.NamesAsked);
    }

    /// <summary>상시 칸에서도 같은 경로로 동작한다.</summary>
    [Fact]
    public async Task WorksOnAPinnedTileToo()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Views2";
        await f.ViewModel.InitializeAsync();

        var pinned = f.ViewModel.Tiles
            .Single(t => t.IsPinned && t.Panel.Entry is not null && t.Panel.Entry.Path == f.CodePath)
            .Panel;

        await pinned.RenameItemCommand.ExecuteAsync(
            pinned.DisplayItems.Single(i => i.Name == "Views"));

        Assert.True(Directory.Exists(Path.Combine(f.CodePath, "Views2")));
        Assert.Contains(pinned.DisplayItems, i => i.Name == "Views2");
    }
}

/// <summary>
/// 이름 바꾼 폴더가 <b>어느 등록의 앵커 자신</b>일 때 (§4.6 309행 · A군 ② B-2 · D12~D15).
/// </summary>
public sealed class RenameAnchorTests
{
    /// <summary>앵커 자신이 목록에 보이게 상위로 올라간 순환 칸.</summary>
    private static async Task<FolderPanelViewModel> AtTheParentOfWorkAsync(MainWindowFixture f)
    {
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.GoUpAsync();
        return panel;
    }

    /// <summary>밀린 디바운스 저장을 밀고 저장 파일을 다시 읽는다 (<c>SetAnchorTests</c> 와 같은 결).</summary>
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

    // ── D12 · path 는 따라간다 ────────────────────────────────────────────────

    [Fact]
    public async Task MovesTheRegistrationPathAndKeepsTheFolderId()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);

        // folderId 는 그대로다 — 새로 만들면 대상함·매크로·타일의 참조가 고아가 된다.
        Assert.Equal(id, f.Workspace.Folders.Single(e => e.Id == id).Id);
        Assert.Equal(5, f.Workspace.Folders.Count);
    }

    /// <summary>저장되는 <c>folders[]</c> 와 화면 쪽 목록이 같이 간다 (§4.11 550~552행).</summary>
    [Fact]
    public async Task BothLayersMove()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, f.ViewModel.Rows.Single(r => r.Entry.Id == id).PathText);
    }

    /// <summary>앱을 다시 열면 새 경로가 그대로다 — 저장을 탔다는 증거.</summary>
    [Fact]
    public async Task SurvivesAReload()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        var reloaded = await ReloadAsync(
            f, f.Workspace.Id, w => w.Folders.Single(e => e.Id == id).Path == expected);

        Assert.Equal(expected, reloaded.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(5, reloaded.Folders.Count);
    }

    /// <summary><c>currentPath</c> 는 이 경로로도 저장 파일에 새어 들지 않는다 (§4.6 292행).</summary>
    [Fact]
    public async Task CurrentPathNeverReachesTheFile()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        await ReloadAsync(f, f.Workspace.Id, w => w.Folders.Single(e => e.Id == id).Path == expected);
        var json = await File.ReadAllTextAsync(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("currentPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("work2", json);
    }

    // ── D13 · D14 확인창 ─────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmingAlsoRenamesTheDisplayName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = true;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("work2", f.Workspace.Folders.Single(e => e.Id == id).DisplayName);

        // 확인창은 딱 한 번 떴다. 새 대화상자를 만들지 않고 D10 경로를 재사용했다는 관찰점이다.
        Assert.Single(f.Prompt.Messages);
    }

    /// <summary>
    /// D14 — 「취소」는 <c>displayName</c> 갱신만 거절한다. 이름 바꾸기 자체와 <c>path</c> 갱신은
    /// 어느 쪽을 눌러도 일어난다.
    /// </summary>
    [Fact]
    public async Task CancelingRefusesOnlyTheDisplayName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = false;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.WorkPath)!, "work2");

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("작업", f.Workspace.Folders.Single(e => e.Id == id).DisplayName);

        // 이름 바꾸기 자체도 path 갱신도 일어났다.
        Assert.True(Directory.Exists(expected));
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
    }

    /// <summary><c>description</c> 은 이 경로의 편집 대상이 아니다.</summary>
    [Fact]
    public async Task DescriptionIsNeverTouched()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = true;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("작업").Entry.Id;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("탐색용", f.Workspace.Folders.Single(e => e.Id == id).Description);
        Assert.Empty(f.FolderEditor.Opened);
    }

    /// <summary>타일 헤더의 앵커 이름도 따라간다 — <c>Entry</c> 에서 계산되는 값이라 손으로 알린다.</summary>
    [Fact]
    public async Task TheTileHeaderFollows()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "work2";
        f.Prompt.Answer = true;
        var panel = await AtTheParentOfWorkAsync(f);

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal("work2", panel.AnchorName);
    }

    // ── D15 · 앵커가 아니면 아무것도 안 한다 ─────────────────────────────────

    [Fact]
    public async Task ANonAnchorFolderNeverAsksAndNeverTouchesFolders()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Recipe2";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var before = f.Workspace.Folders.Select(e => e.Path).ToList();

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        // 확인창이 안 떴다.
        Assert.Empty(f.Prompt.Messages);
        Assert.Equal(before, f.Workspace.Folders.Select(e => e.Path));
    }

    /// <summary>파일은 앵커가 될 수 없다 — 경로가 우연히 겹칠 일도 없지만 확인창부터 안 뜬다.</summary>
    [Fact]
    public async Task AFileNeverAsks()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "Program.cs";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.Empty(f.Prompt.Messages);
    }

    /// <summary>다른 타일에 걸린 등록의 앵커를 옆에서 바꿔도 그 등록이 따라간다.</summary>
    [Fact]
    public async Task WorksOnAnotherTilesAnchor()
    {
        using var f = new MainWindowFixture();
        f.Prompt.NameAnswer = "code2";
        f.Prompt.Answer = false;
        var panel = await AtTheParentOfWorkAsync(f);
        var id = f.Row("code").Entry.Id;
        var expected = Path.Combine(Path.GetDirectoryName(f.CodePath)!, "code2");

        // 순환 칸에서 상시 타일(code)의 앵커 폴더를 바꾼다.
        await panel.RenameItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "code"));

        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
    }
}
