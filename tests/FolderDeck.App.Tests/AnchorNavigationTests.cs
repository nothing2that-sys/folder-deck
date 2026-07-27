namespace FolderDeck.App.Tests;

/// <summary>앵커 / 현재 경로 분리와 앵커 복귀 3경로 (§4.6).</summary>
public sealed class AnchorNavigationTests
{
    [Fact]
    public async Task DescendingUpdatesCurrentPathButNotTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
        Assert.True(panel.IsAwayFromAnchor);

        // 앵커는 그대로 — 실수 클릭 하나가 워크스페이스를 바꾸지 않는다.
        Assert.Equal(f.WorkPath, panel.Entry!.Path);
        Assert.Equal(f.WorkPath, f.Workspace.Folders.Single(x => x.DisplayName == "작업").Path);
        Assert.Equal("Recipe.cs", Assert.Single(panel.Items).Name);
    }

    [Fact]
    public async Task BreadcrumbFoldsTheAnchorAndAppendsSubfolders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        // 앵커에 있을 때는 조각 하나.
        var first = Assert.Single(panel.Breadcrumbs);
        Assert.Equal("작업", first.Name);
        Assert.True(first.IsAnchor);

        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));

        Assert.Equal(["작업", "Recipe"], panel.Breadcrumbs.Select(b => b.Name));
        Assert.True(panel.Breadcrumbs[0].IsAnchor);
        Assert.False(panel.Breadcrumbs[1].IsAnchor);
        Assert.Equal(f.WorkPath, panel.Breadcrumbs[0].Path);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.Breadcrumbs[1].Path);
    }

    [Fact]
    public async Task LeftRowShowsWhereThePanelWanderedTo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        Assert.Null(f.Row("작업").LocationHint);

        var panel = f.Rotating;
        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));

        Assert.Equal("지금 작업 › Recipe 안", f.Row("작업").LocationHint);
        Assert.True(f.Row("작업").HasLocationHint);
    }

    // ── 앵커 복귀 3경로 ──────────────────────────────────────────────────────

    /// <summary>1경로: breadcrumb 첫 조각(앵커 이름) 클릭.</summary>
    [Fact]
    public async Task ReturnsToAnchorViaBreadcrumbFirstSegment()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await panel.NavigateToAsync(panel.Breadcrumbs[0].Path);

        AssertAtAnchor(f, panel);
    }

    /// <summary>2경로: 왼쪽 리스트에서 같은 폴더 재클릭.</summary>
    [Fact]
    public async Task ReturnsToAnchorViaReClickingTheSameRow()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        AssertAtAnchor(f, panel);
    }

    /// <summary>3경로: <c>Alt+Home</c> — XAML 의 KeyBinding 이 부르는 커맨드.</summary>
    [Fact]
    public async Task ReturnsToAnchorViaAltHomeCommand()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await panel.GoToAnchorCommand.ExecuteAsync(null);

        AssertAtAnchor(f, panel);
    }

    [Fact]
    public async Task GoUpLeavesTheAnchorWhenAlreadyAtIt()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        // 상위 이동은 자유다 — 앵커 위로도 갈 수 있다(§4.6).
        await panel.GoUpAsync();

        Assert.Equal(Directory.GetParent(f.WorkPath)!.FullName, panel.CurrentPath);
        Assert.True(panel.IsAwayFromAnchor);

        // 앵커 밖이면 접을 기준이 없으므로 전체 경로를 보여주고, 힌트도 그렇게 말한다.
        Assert.False(panel.Breadcrumbs[0].IsAnchor);
        Assert.StartsWith("앵커 밖", panel.LocationHint);

        await panel.GoToAnchorAsync();
        AssertAtAnchor(f, panel);
    }

    [Fact]
    public async Task GoUpFromASubfolderComesBackToTheAnchor()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);

        await panel.GoUpAsync();

        AssertAtAnchor(f, panel);
    }

    [Fact]
    public async Task CannotGoAboveADriveRoot()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var root = Path.GetPathRoot(f.WorkPath)!;
        await panel.NavigateToAsync(root);

        Assert.False(panel.CanGoUp);
        await panel.GoUpAsync();
        Assert.Equal(root, panel.CurrentPath);
    }

    /// <summary>
    /// 세션 동작: 실행 중에는 하위 경로를 기억하고, 앱 재시작 시 앵커로 리셋된다(§4.6).
    /// <c>currentPath</c> 를 저장하지 않으므로 그냥 그렇게 된다.
    /// </summary>
    [Fact]
    public async Task CurrentPathIsNeverPersisted()
    {
        using var f = new MainWindowFixture();
        var panel = await DescendedRotatingPanel(f);
        var subPath = panel.CurrentPath;

        // 실제 앱과 같은 저장 경로를 태운다.
        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        f.Store.Flush();

        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));
        Assert.DoesNotContain("currentPath", json);
        Assert.DoesNotContain("Recipe", json);

        // 다시 연 것과 같은 상태 = 앵커.
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var anchor = reloaded.Folders.Single(x => x.DisplayName == "작업").Path;
        Assert.Equal(f.WorkPath, anchor);
        Assert.NotEqual(subPath, anchor);
    }

    [Fact]
    public async Task PinnedPanelKeepsItsOwnPositionIndependentOfTheRotatingOne()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var pinned = f.ViewModel.Panels[0];

        // 상시 패널을 하위로 넣고, 순환 칸은 다른 폴더로 보낸다.
        await pinned.OpenAsync(pinned.Items.Single(i => i.Name == "Views"));
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal(Path.Combine(f.CodePath, "Views"), pinned.CurrentPath);
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);

        await pinned.GoToAnchorAsync();
        Assert.Equal(f.CodePath, pinned.CurrentPath);
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
    }

    private static async Task<ViewModels.FolderPanelViewModel> DescendedRotatingPanel(MainWindowFixture f)
    {
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.OpenAsync(panel.Items.Single(i => i.Name == "Recipe"));
        Assert.True(panel.IsAwayFromAnchor);
        return panel;
    }

    private static void AssertAtAnchor(MainWindowFixture f, ViewModels.FolderPanelViewModel panel)
    {
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);
        Assert.Null(panel.LocationHint);
        Assert.True(Assert.Single(panel.Breadcrumbs).IsAnchor);
        Assert.Null(f.Row("작업").LocationHint);
    }
}
