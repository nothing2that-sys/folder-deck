namespace FolderDeck.App.Tests;

/// <summary>
/// 우클릭 메뉴 「열기」·「속성」 (§4.6 · A군 ① D11). <b>둘 다 읽기다</b> — <c>folders[]</c> 도
/// 앵커도 바뀌지 않는다.
/// </summary>
public sealed class OpenAndPropertiesTests
{
    /// <summary>
    /// 「열기」는 더블클릭과 <b>같은 커맨드</b>다 (D11). 폴더면 타일 안에서 하위로 들어간다 —
    /// 탐색기 별창은 「탐색기에서 열기」의 일이고 여기서 겹치면 안 된다.
    /// </summary>
    [Fact]
    public async Task OpenOnAFolderDescendsInsideTheTile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), panel.CurrentPath);
        Assert.True(panel.IsAwayFromAnchor);

        // 별창도, 셸 실행도 없다.
        Assert.Empty(f.Shell.Revealed);
        Assert.Empty(f.Shell.Opened);
    }

    /// <summary>파일이면 셸 기본 연결로 실행한다 — 탐색기 별창이 아니다.</summary>
    [Fact]
    public async Task OpenOnAFileGoesToTheShellNotToExplorer()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), Assert.Single(f.Shell.Opened));
        Assert.Empty(f.Shell.Revealed);

        // 파일을 열어도 보고 있는 폴더는 그대로다.
        Assert.Equal(f.DocsPath, panel.CurrentPath);
    }

    /// <summary>「열기」가 앵커를 건드리지 않는다 — 읽기다.</summary>
    [Fact]
    public async Task OpenNeverTouchesTheAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(f.WorkPath, panel.Entry!.Path);
        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
    }

    [Fact]
    public async Task PropertiesPassesTheFilePathToTheShell()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems.Single(i => i.Name == "spec.md"));

        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), Assert.Single(f.Shell.Properties));
    }

    /// <summary>폴더에도 뜻이 있다 — 감추지 않는다.</summary>
    [Fact]
    public async Task PropertiesWorksOnAFolderToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), Assert.Single(f.Shell.Properties));
    }

    /// <summary>속성은 아무것도 바꾸지 않는다 — 열지도, 탐색기를 띄우지도, 이동하지도 않는다.</summary>
    [Fact]
    public async Task PropertiesChangesNothingElse()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Empty(f.Shell.Opened);
        Assert.Empty(f.Shell.Revealed);
        Assert.Equal(f.WorkPath, panel.CurrentPath);
        Assert.Equal(f.WorkPath, panel.Entry!.Path);
    }

    [Fact]
    public async Task PropertiesFailureIsSurfacedNotSwallowed()
    {
        using var f = new MainWindowFixture();
        f.Shell.Error = "속성 창을 열 수 없다";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        var panel = f.Rotating;

        panel.ShowItemProperties(panel.DisplayItems[0]);

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("속성 창을 열 수 없다", f.ViewModel.Message);
    }

    [Fact]
    public void NullItemIsIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        panel.ShowItemProperties(null);

        Assert.Empty(f.Shell.Properties);
    }

    /// <summary>검색 결과의 하위 폴더 항목에도 같은 메뉴가 전체 경로로 걸린다 (§4.10).</summary>
    [Fact]
    public async Task WorksOnSearchResultsToo()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.IncludeSubfolders = true;
        panel.SearchText = "NumberBox.xaml";
        for (var i = 0; i < 200 && (panel.IsSearching || panel.DisplayItems.Count == 0); i++)
        {
            await Task.Delay(25);
        }

        panel.ShowItemProperties(Assert.Single(panel.DisplayItems));

        Assert.Equal(
            Path.Combine(f.WorkPath, "Recipe", "Controls", "NumberBox.xaml"),
            Assert.Single(f.Shell.Properties));
    }
}
