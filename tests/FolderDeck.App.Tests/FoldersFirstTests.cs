using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// "폴더 먼저"를 끄면 폴더와 파일이 한 덩어리로 정렬된다 (Phase 8-B ②).
/// </summary>
/// <remarks>
/// <b>기본값은 예전 동작이다</b> — 켜져 있고, 폴더가 위에 뭉친다. 끄는 길은
/// <see cref="FolderPanelViewModel.ToggleFoldersFirstCommand"/> 하나이고 그것이 목록을 다시 읽는다.
/// <para>
/// <b>저장하지 않는다.</b> 워크스페이스 스키마에 슬롯을 새로 파지 않기로 했으므로 창을 다시
/// 열면 켜진 상태로 돌아온다 — 아래 마지막 판이 그것을 못박는다.
/// </para>
/// </remarks>
public sealed class FoldersFirstTests
{
    [Fact]
    public async Task FoldersComeFirstByDefault()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.True(code.FoldersFirst);
        Assert.Equal(["Recipe", "Views", "Main.cs"], code.Items.Select(i => i.Name));
    }

    /// <summary>끄면 폴더가 파일 사이로 섞여 든다 — 이름 순서 하나로 다시 선다.</summary>
    [Fact]
    public async Task TurningItOffMergesFoldersIntoTheFiles()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.False(code.FoldersFirst);

        // 목록이 실제로 다시 읽혔다 — Main.cs 가 Recipe 앞으로 올라왔다.
        Assert.Equal(["Main.cs", "Recipe", "Views"], code.Items.Select(i => i.Name));
    }

    /// <summary>다시 누르면 되돌아온다.</summary>
    [Fact]
    public async Task TogglingBackRestoresTheGrouping()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.True(code.FoldersFirst);
        Assert.Equal(["Recipe", "Views", "Main.cs"], code.Items.Select(i => i.Name));
    }

    /// <summary>
    /// 저장되지 않는 상태라 화면이 말해 주지 않으면 알 길이 없다 — 메타 문구에 "통합"이 붙는다.
    /// </summary>
    [Fact]
    public async Task TheMetaLineSaysWhenItIsOff()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(" · 이름 ↑", code.MetaSortText);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        Assert.Equal(" · 이름 ↑ · 통합", code.MetaSortText);

        // 세 조각을 이어 붙인 것이 메타 줄이라는 규칙은 여기서도 선다.
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
    }

    /// <summary>정렬 기준을 바꿔도 통합 여부는 남는다 — 두 값은 서로 독립이다.</summary>
    [Fact]
    public async Task ChangingTheCriterionKeepsTheGroupingChoice()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.False(code.FoldersFirst);
        Assert.Equal(SortBy.Modified, code.Entry!.SortBy);
    }

    /// <summary>타일마다 따로다 — 옆 칸이 따라 바뀌지 않는다 (§3.1 · 정렬과 같은 결).</summary>
    [Fact]
    public async Task EachTileGroupsOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.Panels[0].ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.False(f.ViewModel.Panels[0].FoldersFirst);
        Assert.True(f.ViewModel.Panels[1].FoldersFirst);
    }

    /// <summary>
    /// <b>저장하지 않는다.</b> 스키마에 슬롯이 없다 — 파일에는 정렬 기준·방향만 남는다.
    /// </summary>
    [Fact]
    public async Task TheChoiceIsNotWrittenToTheWorkspaceFile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal(0, saves);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        // 이 폴더의 정렬 슬롯은 그대로다 — 통합 여부를 적은 자리가 없다.
        Assert.Equal(SortBy.Name, entry.SortBy);
        Assert.False(entry.SortDesc);
    }

    /// <summary>아직 폴더를 안 띄운 순환 칸에는 바꿀 것이 없다.</summary>
    [Fact]
    public async Task AnEmptyPanelHasNothingToRegroup()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        await rotating.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.True(rotating.FoldersFirst);
    }
}
