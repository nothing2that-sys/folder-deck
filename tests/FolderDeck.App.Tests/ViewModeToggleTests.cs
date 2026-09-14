using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일 헤더 메타의 뷰 글자를 눌러 네 뷰 방식을 순환한다 (§3.2 · §7.1 <c>folders[].viewMode</c>).
/// Stage 3D 까지 <c>viewMode</c> 는 읽기만 하는 값이어서 JSON 을 손으로 고쳐야 바뀌었다.
/// </summary>
public sealed class ViewModeToggleTests
{
    [Fact]
    public async Task TogglingAdvancesTheValueAndSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(FolderViewMode.Details, code.Entry!.ViewMode);

        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);

        Assert.Equal(FolderViewMode.ExtraLargeIcons, code.Entry.ViewMode);

        // 저장은 pinned·배치가 이미 쓰는 디바운스 통로로 간다 — 새 경로를 만들지 않았다.
        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(
            FolderViewMode.ExtraLargeIcons,
            reloaded.Folders.Single(e => e.Path == f.CodePath).ViewMode);
    }

    /// <summary>
    /// 네 <c>ListView</c> 의 <c>Visibility</c> 를 가르는 값 넷과 헤더 문구가 <b>함께</b> 움직인다.
    /// 하나만 갱신되면 목록과 문구가 어긋난다.
    /// </summary>
    [Fact]
    public async Task FiveIndicatorsMoveTogether()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.ToggleViewModeCommand.Execute(null);
        seen.Clear();
        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains(nameof(FolderPanelViewModel.IsDetailsView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.IsListView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.IsLargeIconView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.IsExtraLargeIconView), seen);
        Assert.Contains(nameof(FolderPanelViewModel.MetaText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.ViewModeText), seen);

        Assert.False(code.IsDetailsView);
        Assert.False(code.IsListView);
        Assert.False(code.IsLargeIconView);
        Assert.True(code.IsExtraLargeIconView);
        Assert.Equal("extraLargeIcons", code.ViewModeText);
        Assert.Contains("extraLargeIcons", code.MetaText);
        Assert.DoesNotContain("details", code.MetaText);
    }

    /// <summary>메타 세 조각을 이어 붙인 것이 문구다 — 뷰가 그리는 줄과 어긋날 수 없다.</summary>
    [Fact]
    public async Task MetaPartsJoinIntoTheMetaLine()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);

        code.ToggleViewModeCommand.Execute(null);
        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
    }

    [Fact]
    public async Task TogglingFourTimesComesBack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);
        code.ToggleViewModeCommand.Execute(null);

        Assert.Equal(FolderViewMode.Details, code.Entry!.ViewMode);
        Assert.True(code.IsDetailsView);
        Assert.False(code.IsListView);
    }

    /// <summary>뷰 방식은 폴더별 속성이다 — 옆 타일이 따라 바뀌지 않는다 (§3.1).</summary>
    [Fact]
    public async Task EachTileFlipsOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var output = f.ViewModel.Panels[1];

        code.ToggleViewModeCommand.Execute(null);

        Assert.True(code.IsLargeIconView);
        Assert.True(output.IsDetailsView);
        Assert.Equal(FolderViewMode.Details, output.Entry!.ViewMode);
    }

    /// <summary>
    /// 전환하면 선택이 비워진다 — 네 <c>ListView</c> 가 각자 선택을 들고 있어서 그대로 두면
    /// 보이게 된 목록에는 아무것도 안 골라져 있는데 헤더는 "N개 선택"을 말한다. v1 은 잇지 않는다.
    /// </summary>
    [Fact]
    public async Task TogglingClearsTheSelection()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [code.Items[0]];
        Assert.Same(code, f.ViewModel.SelectionOwner);

        var cleared = 0;
        code.SelectionCleared += (_, _) => cleared++;

        code.ToggleViewModeCommand.Execute(null);

        // 목록에서도 실제로 풀리도록 뷰에 알린다(네 ListView 를 다 훑는 그 통로다).
        Assert.Equal(1, cleared);
        Assert.Empty(code.SelectedItems);
        Assert.False(code.HasSelection);
        Assert.Null(f.ViewModel.SelectionOwner);
    }

    /// <summary>
    /// 고른 것이 없을 때 전환하면 <b>선택 통지가 아예 안 나간다</b> — 상태 바와 저장에 잡음을
    /// 만들지 않는다. 저장은 전환 한 번에 한 번이다.
    /// </summary>
    [Fact]
    public async Task TogglingWithNoSelectionMakesNoSelectionNoise()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.HasSelection);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.ToggleViewModeCommand.Execute(null);

        Assert.DoesNotContain(nameof(FolderPanelViewModel.SelectedItems), seen);
        Assert.DoesNotContain(nameof(FolderPanelViewModel.SelectionText), seen);
        Assert.Equal(1, saves);
    }

    /// <summary>아직 폴더를 안 띄운 순환 칸에는 뒤집을 값이 없다 — 저장도 일어나지 않는다.</summary>
    [Fact]
    public void AnEmptyRotatingPanelHasNothingToFlip()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        var saves = 0;
        rotating.SaveFolderEntry = () => saves++;

        rotating.ToggleViewModeCommand.Execute(null);

        Assert.False(rotating.IsDetailsView);
        Assert.False(rotating.IsListView);
        Assert.False(rotating.IsLargeIconView);
        Assert.False(rotating.IsExtraLargeIconView);
        Assert.Null(rotating.MetaText);
        Assert.Equal(0, saves);
    }

    /// <summary>
    /// 「더 보기」 메뉴의 목록/자세히 체크 항목이 부르는 <c>SetViewModeCommand</c>(Phase 20 ·
    /// D120 · T17, 대조 시험) — 같은 값으로 두 번 불러도 값이 그대로다. R6(D120 §3) 이
    /// 이것을 "뒤집기"로 바꾸면 두 번째 호출에서 값이 달아나며 이 시험이 빨개진다.
    /// <b>지금은 이미 초록이어도 정상이다</b> — <c>SetViewMode</c> 자체가 이미 그렇게
    /// 짜여 있다(D119, 같은 값이면 <c>Entry.ViewMode == mode</c> 가드에 걸려 아무 일도
    /// 안 한다).
    /// </summary>
    [Fact]
    public async Task SettingTheSameValueTwiceLeavesItUnchanged()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(FolderViewMode.Details, code.Entry!.ViewMode);

        code.SetViewModeCommand.Execute(FolderViewMode.Details);
        Assert.Equal(FolderViewMode.Details, code.Entry.ViewMode);

        code.SetViewModeCommand.Execute(FolderViewMode.Details);
        Assert.Equal(FolderViewMode.Details, code.Entry.ViewMode);
    }

    [Fact]
    public async Task TheMenuCommandCanSelectLargeIconsDirectly()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SetViewModeCommand.Execute(FolderViewMode.LargeIcons);

        Assert.Equal(FolderViewMode.LargeIcons, code.Entry!.ViewMode);
        Assert.True(code.IsLargeIconView);
        Assert.Equal("▩", code.ViewModeIcon);
        Assert.Contains("큰 아이콘", code.ViewModeTip);
    }

    [Fact]
    public async Task TheMenuCommandCanSelectExtraLargeIconsDirectly()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SetViewModeCommand.Execute(FolderViewMode.ExtraLargeIcons);

        Assert.Equal(FolderViewMode.ExtraLargeIcons, code.Entry!.ViewMode);
        Assert.True(code.IsExtraLargeIconView);
        Assert.False(code.IsLargeIconView);
        Assert.Equal("▣", code.ViewModeIcon);
        Assert.Contains("아주 큰 아이콘", code.ViewModeTip);
    }
}
