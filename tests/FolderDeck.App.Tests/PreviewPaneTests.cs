using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 타일 하단 미리보기 칸의 켜고 끄기·비율 저장 (Phase 17 · D86 · design.md §3.2). 회차 2a 는
/// 자리만 만드므로 여기서는 <see cref="FolderPanelViewModel.ToggleShowPreviewCommand"/>·
/// <see cref="FolderPanelViewModel.SetPreviewRatio"/> 가 정확한 상태를 만드는지만 문다 —
/// <see cref="ColumnVisibilityTests"/> 와 같은 결이다.
/// </summary>
/// <remarks>
/// <b>기본이 꺼짐인 것이 <see cref="ColumnVisibilityTests"/> 와 다른 점이다</b> — `showSize`
/// 형제들은 기본이 켜짐이라 "끄면 뒤집힌다"였지만, 미리보기는 기본이 꺼짐이라 "켜면
/// 뒤집힌다"다(§7.1 v9). 검색 타일은 <c>Entry</c> 가 없어 저장되지 않는다(§7.1 v7).
/// </remarks>
public sealed class PreviewPaneTests
{
    /// <summary>패널만 떼어 만든다 — <see cref="ColumnVisibilityTests"/> 의 <c>BareSearchPanel</c> 과 같은 결.</summary>
    private static FolderPanelViewModel BareSearchPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: null);

    /// <summary>패널만 떼어 만든다 — 검색 타일이 아닌 쪽.</summary>
    private static FolderPanelViewModel BareFolderPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: false,
        reportError: null,
        reportRejection: null);

    [Fact]
    public async Task TurningOnShowPreviewFlipsTheFolderEntryAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.Entry!.ShowPreview);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        code.ToggleShowPreviewCommand.Execute(null);

        Assert.True(code.Entry!.ShowPreview);
        Assert.Equal(1, saves);
    }

    /// <summary>다시 누르면 되돌아온다.</summary>
    [Fact]
    public async Task TogglingPreviewBackTurnsItOff()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        code.ToggleShowPreviewCommand.Execute(null);
        code.ToggleShowPreviewCommand.Execute(null);

        Assert.False(code.Entry!.ShowPreview);
    }

    /// <summary>값은 폴더마다다 — 순환 칸을 다른 폴더로 갈아타면 그 폴더의 값으로 갈아탄다.</summary>
    [Fact]
    public async Task ThePreviewChoiceTravelsWithTheFolderInARotatingSlot()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        Assert.False(panel.Entry!.ShowPreview);

        panel.ToggleShowPreviewCommand.Execute(null);
        Assert.True(panel.Entry!.ShowPreview);

        // 다른 폴더로 갈아탄다 — 그 폴더는 기본값(꺼짐) 그대로다.
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.False(panel.Entry!.ShowPreview);

        // 되돌아가면 켜둔 값이 그대로 남아 있다.
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.True(panel.Entry!.ShowPreview);
    }

    /// <summary>타일마다 따로다 — 옆 칸이 따라 바뀌지 않는다.</summary>
    [Fact]
    public async Task EachTileTogglesPreviewOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.Panels[0].ToggleShowPreviewCommand.Execute(null);

        Assert.True(f.ViewModel.Panels[0].Entry!.ShowPreview);
        Assert.False(f.ViewModel.Panels[1].Entry!.ShowPreview);
    }

    /// <summary>저장되므로 다시 열어도 남는다.</summary>
    [Fact]
    public async Task ThePreviewChoiceSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.ToggleShowPreviewCommand.Execute(null);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.True(entry.ShowPreview);

        // 옆 슬롯은 건드리지 않았다.
        Assert.True(entry.ShowSize);
    }

    /// <summary>아직 폴더를 안 띄운 순환 칸에는 바꿀 것이 없다.</summary>
    [Fact]
    public void AnEmptyPanelHasNothingToTogglePreview()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);
        Assert.Null(rotating.Entry);

        // Entry 가 아예 없을 때의 폴백이 꺼짐인지 — 형제들의 `?? true` 를 그대로 따라가면
        // 안 켠 기능이 켜진 것처럼 보인다(§7.1 v9).
        Assert.False(rotating.CurrentShowPreview);

        rotating.ToggleShowPreviewCommand.Execute(null);

        Assert.True(rotating.IsEmpty);
    }

    /// <summary>
    /// 폴더 타일에서 <see cref="FolderPanelViewModel.CurrentShowPreview"/> 는 <c>Entry</c> 를
    /// 그대로 따른다. 새 <see cref="FolderEntry"/> 를 붙인 직후에는 기본값(꺼짐)이어야 한다 —
    /// <see cref="ColumnVisibilityTests"/> 의 형제 시험과 달리 여기서는 <c>false</c> 다.
    /// </summary>
    [Fact]
    public void CurrentShowPreviewFollowsTheFolderEntry()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };

        Assert.False(panel.CurrentShowPreview);

        panel.Entry.ShowPreview = true;

        Assert.True(panel.CurrentShowPreview);
    }

    /// <summary>
    /// 검색 타일에서 토글해도 <c>Entry</c> 도 저장도 건드리지 않는다 — 검색 타일엔 <c>Entry</c>
    /// 자체가 없다(§7.1 v7).
    /// </summary>
    [Fact]
    public void TogglingPreviewOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowPreviewCommand.Execute(null);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);

        // Entry 가 null 이라 조기 반환해도 위 둘은 우연히 참일 수 있다 — 검색 타일 갈래가
        // 실제로 탔는지(자체 필드가 뒤집혔는지)까지 물어야 그 갈래가 없어져도 잡힌다.
        Assert.True(panel.CurrentShowPreview);
    }

    /// <summary>
    /// 검색 타일에서 토글하면 <see cref="FolderPanelViewModel.CurrentShowPreview"/> 자체가
    /// 뒤집힌다 — <c>ToggleShowSize</c> 형제가 이미 이렇게 한다(D79-b-fix).
    /// </summary>
    [Fact]
    public void TogglingPreviewOnASearchTileFlipsCurrentShowPreview()
    {
        var panel = BareSearchPanel();
        Assert.False(panel.CurrentShowPreview);

        panel.ToggleShowPreviewCommand.Execute(null);
        Assert.True(panel.CurrentShowPreview);

        panel.ToggleShowPreviewCommand.Execute(null);
        Assert.False(panel.CurrentShowPreview);
    }

    [Fact]
    public void SettingThePreviewRatioFlipsTheFolderEntryAndSavesOnce()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.SetPreviewRatio(0.6);

        Assert.Equal(0.6, panel.Entry.PreviewRatio);
        Assert.Equal(1, saves);
    }

    /// <summary>
    /// 검색 타일 쪽 <c>searchPreviewRatio</c> 는 <see cref="FolderEntry.PreviewRatio"/> 처럼
    /// 스스로 클램프하는 setter 가 없다 — <see cref="FolderPanelViewModel.SetPreviewRatio"/>
    /// 가 그 자리에서 같은 판정(§7.1 v9)을 대신 한다.
    /// </summary>
    [Fact]
    public void AnOutOfRangePreviewRatioFallsBackToTheDefault()
    {
        var panel = BareSearchPanel();

        panel.SetPreviewRatio(1.5);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, panel.CurrentPreviewRatio);

        panel.SetPreviewRatio(-0.2);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, panel.CurrentPreviewRatio);
    }

    /// <summary>저장되므로 다시 열어도 남는다.</summary>
    [Fact]
    public async Task ThePreviewRatioSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SetPreviewRatio(0.6);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.Equal(0.6, entry.PreviewRatio);

        // 옆 슬롯은 건드리지 않았다.
        Assert.True(entry.ShowSize);
    }

    /// <summary>
    /// 검색 타일에서 비율을 바꿔도 <c>Entry</c> 도 저장도 건드리지 않는다 — 검색 타일엔
    /// <c>Entry</c> 자체가 없다(§7.1 v7).
    /// </summary>
    [Fact]
    public void SettingThePreviewRatioOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.SetPreviewRatio(0.6);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);
    }

    /// <summary>
    /// 순환 칸이 폴더를 갈아탈 때 실제로 벌어지는 일 — <c>DataContext</c> 는 안 바뀌고
    /// <c>Entry</c> 만 갈린다(D93-b). 값이 맞는지가 아니라 <c>PropertyChanged</c> 가 그
    /// 이름으로 나가는지를 문다 — <see cref="ColumnVisibilityTests.TogglingShowPositionDuringARecursiveSearchNotifiesCurrentShowPosition"/>
    /// 과 같은 결이다. 통지가 안 나가면 화면(코드비하인드의 <c>ApplyPreviewLayout</c>)이
    /// 다시 서지 않는다.
    /// </summary>
    [Fact]
    public void AssigningTheEntryNotifiesCurrentShowPreview()
    {
        var panel = BareFolderPanel();

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.Entry = new FolderEntry { Path = @"D:\x", ShowPreview = true, PreviewRatio = 0.6 };

        Assert.Contains(nameof(FolderPanelViewModel.CurrentShowPreview), seen);
    }

    /// <summary><see cref="AssigningTheEntryNotifiesCurrentShowPreview"/> 와 같은 결·같은 이유다.</summary>
    [Fact]
    public void AssigningTheEntryNotifiesCurrentPreviewRatio()
    {
        var panel = BareFolderPanel();

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.Entry = new FolderEntry { Path = @"D:\x", ShowPreview = true, PreviewRatio = 0.6 };

        Assert.Contains(nameof(FolderPanelViewModel.CurrentPreviewRatio), seen);
    }
}
