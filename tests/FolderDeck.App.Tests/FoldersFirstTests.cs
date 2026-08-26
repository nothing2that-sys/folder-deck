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
/// <b>폴더마다이고 저장된다</b> (Phase 8-E ① · <c>folders[].foldersFirst</c>). 8-B~8-D 동안에는
/// 런타임 상태였다 — <see cref="TheChoiceSurvivesAReopen"/> 이 그 왕복이 없어진 것을 못박는다.
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
    /// 화면이 말해 준다 — 메타 문구에 "통합"이 붙는다(헤더 1줄에서는 정렬 아이콘의 <c>∪</c>).
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
    /// <b>저장된다</b> (Phase 8-E ①). 8-B~8-D 동안에는 런타임 상태라 창을 다시 열면 켜진 채로
    /// 돌아왔다 — <c>folders[].foldersFirst</c> 슬롯이 생기면서 그 왕복이 없어졌다.
    /// </summary>
    /// <remarks>
    /// 저장 통로는 정렬이 이미 쓰던 것 그대로다 — 새 경로를 만들지 않았다.
    /// </remarks>
    [Fact]
    public async Task TheChoiceSurvivesAReopen()
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

        // 정렬 변경과 같은 규칙 — 한 번 바꾸면 저장도 한 번이다.
        Assert.Equal(1, saves);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.False(entry.FoldersFirst);

        // 옆 슬롯은 건드리지 않았다.
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
