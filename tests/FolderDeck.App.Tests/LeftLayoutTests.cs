using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>왼쪽 컬럼 수동 접기 — 워크스페이스에 저장된다(자동 접힘 없음).</summary>
public sealed class LeftLayoutTests
{
    [Fact]
    public void NothingIsCollapsedByDefault()
    {
        using var f = new MainWindowFixture();

        Assert.False(f.ViewModel.FoldersCollapsed);
        Assert.False(f.ViewModel.LowerCollapsed);
        Assert.True(f.ViewModel.ShowLeftSplitter);

        // 아직 슬롯을 만들지 않았다 — 건드리지 않은 워크스페이스는 그대로 둔다.
        Assert.Null(f.Workspace.LeftLayout);
    }

    [Fact]
    public void EachAreaTogglesIndependently()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ToggleFolders();
        Assert.True(f.ViewModel.FoldersCollapsed);
        Assert.False(f.ViewModel.LowerCollapsed);

        f.ViewModel.ToggleLower();
        Assert.True(f.ViewModel.LowerCollapsed);

        f.ViewModel.ToggleFolders();
        Assert.False(f.ViewModel.FoldersCollapsed);
        Assert.True(f.ViewModel.LowerCollapsed);
    }

    [Fact]
    public void SplitterHidesWhenEitherSideIsCollapsed()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.FoldersCollapsed = true;
        Assert.False(f.ViewModel.ShowLeftSplitter);

        f.ViewModel.FoldersCollapsed = false;
        Assert.True(f.ViewModel.ShowLeftSplitter);

        f.ViewModel.LowerCollapsed = true;
        Assert.False(f.ViewModel.ShowLeftSplitter);
    }

    [Fact]
    public void CollapsedStateSurvivesAReopen()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.FoldersCollapsed = true;
        f.ViewModel.LowerCollapsed = true;
        f.Store.Flush();

        // 다시 연 것과 같은 상태.
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var reopened = new MainViewModel(
            reloaded, f.Store, new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
            f.Engine, f.Prompt);

        Assert.True(reopened.FoldersCollapsed);
        Assert.True(reopened.LowerCollapsed);
    }

    [Fact]
    public void OnlyTheCollapsedSideIsRemembered()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.LowerCollapsed = true;
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.False(reloaded.LeftLayout!.FoldersCollapsed);
        Assert.True(reloaded.LeftLayout.LowerCollapsed);
    }

    /// <summary>슬롯이 없던 v1 워크스페이스도 접으면 그때 슬롯이 생기고 v2 로 저장된다.</summary>
    [Fact]
    public void CollapsingCreatesTheSlotOnAWorkspaceThatLacksIt()
    {
        using var f = new MainWindowFixture();
        Assert.Null(f.Workspace.LeftLayout);

        f.ViewModel.FoldersCollapsed = true;

        Assert.NotNull(f.Workspace.LeftLayout);
        f.Store.Flush();
        Assert.Equal(
            Workspace.CurrentSchemaVersion,
            f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow().SchemaVersion);
    }

    /// <summary>접는 것은 표시일 뿐 — 폴더 리스트의 컨트롤 흐름은 그대로 살아 있다.</summary>
    [Fact]
    public async Task CollapsingDoesNotBreakTheRotatingPanelControlFlow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.FoldersCollapsed = true;

        // 행은 여전히 있고 클릭 경로도 동작한다(접었으면 펴서 쓰면 된다).
        Assert.Equal(5, f.ViewModel.Rows.Count);
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
    }

    [Fact]
    public void TogglingTheSameValueTwiceDoesNotChurn()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.FoldersCollapsed = true;
        f.ViewModel.FoldersCollapsed = true;

        Assert.True(f.ViewModel.FoldersCollapsed);
    }
}
