namespace FolderDeck.App.Tests;

/// <summary>탐색 히스토리 — 마우스 뒤로/앞으로. 런타임 전용이라 저장하지 않는다.</summary>
public sealed class HistoryTests
{
    [Fact]
    public async Task NothingToGoBackToAtTheStart()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var pinned = f.ViewModel.Panels[0];
        Assert.False(pinned.CanGoBack);
        Assert.False(pinned.CanGoForward);
        Assert.False(pinned.GoBackCommand.CanExecute(null));

        // 스택 끝에서 눌러도 무동작 — 자리를 지킨다.
        await pinned.GoBackAsync();
        Assert.Equal(f.CodePath, pinned.CurrentPath);
    }

    /// <summary>하위 2단 들어갔다 뒤로 두 번 → 원래 자리.</summary>
    [Fact]
    public async Task TwoLevelsDownThenTwiceBackReturnsToStart()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));
        await panel.OpenAsync(panel.DisplayItems.Single(i => i.Name == "Controls"));
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe", "Controls"), panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);
    }

    [Fact]
    public async Task ForwardGoesBackToWhereYouCameFrom()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));
        await panel.GoBackAsync();
        Assert.True(panel.CanGoForward);

        await panel.GoForwardAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
        Assert.False(panel.CanGoForward);
    }

    /// <summary>
    /// 뒤로는 상위 이동과 다르다 — 형제 폴더로 갔다 뒤로 누르면 부모가 아니라 직전 위치다.
    /// </summary>
    [Fact]
    public async Task BackIsNotTheSameAsUp()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        var recipe = Path.Combine(f.WorkPath, "Recipe");
        var views = Path.Combine(f.WorkPath, "Views");

        await panel.NavigateToAsync(recipe);
        await panel.NavigateToAsync(views);   // 형제로 이동
        Assert.Equal(views, panel.CurrentPath);

        await panel.GoBackAsync();

        // 부모(code)가 아니라 형제였던 Recipe 로 돌아온다.
        Assert.Equal(recipe, panel.CurrentPath);
        Assert.NotEqual(f.WorkPath, panel.CurrentPath);
    }

    [Fact]
    public async Task GoingSomewhereNewClearsTheForwardStack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        await panel.GoBackAsync();
        Assert.True(panel.CanGoForward);

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));

        Assert.False(panel.CanGoForward);
        Assert.True(panel.CanGoBack);
    }

    [Fact]
    public async Task UpAndAnchorReturnAreBothPushed()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        await panel.GoUpAsync();               // 상위 → 히스토리 push
        Assert.Equal(f.WorkPath, panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);

        await panel.GoToAnchorAsync();         // 앵커 복귀 → 히스토리 push
        Assert.Equal(f.WorkPath, panel.CurrentPath);

        await panel.GoBackAsync();
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
    }

    /// <summary>
    /// 왼쪽 리스트로 다른 폴더를 띄우는 것도 히스토리다 — 뒤로 가면 그때의 앵커까지 돌아온다.
    /// </summary>
    [Fact]
    public async Task BackUndoesSwitchingFoldersFromTheLeftList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var panel = f.Rotating;

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Equal(f.DocsPath, panel.CurrentPath);

        await panel.GoBackAsync();

        // 경로만이 아니라 앵커·뷰 방식까지 code 로 되돌아온다.
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.Same(f.Row("작업").Entry, panel.Entry);
        Assert.True(panel.IsDetailsView);
    }

    /// <summary>제자리로 가는 이동은 쌓지 않는다 — 뒤로가 제자리걸음이면 고장으로 느껴진다.</summary>
    [Fact]
    public async Task ReturningToTheSamePlaceIsNotPushed()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        // 처음 띄운 직후엔 뒤로 갈 곳이 없다(그전 위치가 없으므로).
        Assert.False(panel.CanGoBack);

        await panel.GoToAnchorAsync();   // 이미 앵커에 있다 — 쌓이지 않아야 한다
        await panel.GoToAnchorAsync();

        Assert.False(panel.CanGoBack);

        // 뒤로는 무동작이고 자리를 지킨다.
        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
    }

    [Fact]
    public async Task EachPanelKeepsItsOwnHistory()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var pinned = f.ViewModel.Panels[0];
        var rotating = f.Rotating;

        await pinned.NavigateToAsync(Path.Combine(f.WorkPath, "Views"));
        Assert.True(pinned.CanGoBack);
        Assert.False(rotating.CanGoBack);

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        await rotating.GoBackAsync();

        // 순환 패널의 뒤로가 상시 패널을 건드리지 않는다.
        Assert.Equal(Path.Combine(f.WorkPath, "Views"), pinned.CurrentPath);
    }

    [Fact]
    public async Task HistoryIsNotPersisted()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));
        Assert.True(panel.CanGoBack);

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        f.Store.Flush();
        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("history", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recipe", json);

        // 새로 띄운 뷰모델(= 재시작)에는 히스토리가 없다.
        using var restarted = new MainWindowFixture();
        await restarted.ViewModel.InitializeAsync();
        Assert.False(restarted.ViewModel.Panels[0].CanGoBack);
        Assert.False(restarted.Rotating.CanGoBack);
    }

    [Fact]
    public async Task RefreshDoesNotTouchHistory()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        await panel.NavigateToAsync(Path.Combine(f.WorkPath, "Recipe"));

        await f.ViewModel.RefreshAllAsync();

        await panel.GoBackAsync();
        Assert.Equal(f.WorkPath, panel.CurrentPath);
    }
}
