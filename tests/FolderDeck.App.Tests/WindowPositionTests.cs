using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>
/// 창 위치 저장 (§6.1 · Phase 4 Stage 2). 창 자체는 만들지 않는다 — 뷰가 잰 사각형을 받아
/// 저장에 태우는 부분만 본다. 방어 판정은 <c>FolderDeck.Core.Tests.WindowPlacementTests</c>.
/// </summary>
public sealed class WindowPositionTests
{
    private static ScreenRect Rect(double x, double y, double w, double h) => new(x, y, w, h);

    [Fact]
    public void MovingTheWindowWritesThePositionIntoTheWorkspace()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: false);

        var window = f.Workspace.Window;
        Assert.NotNull(window);
        Assert.Equal((300d, 200d, 1400d, 900d), (window.X, window.Y, window.Width, window.Height));
        Assert.False(window.Maximized);
    }

    [Fact]
    public void ThePositionSurvivesAReopen()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(-1340, -171, 1200, 2000), maximized: false);
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        Assert.Equal(-1340, reloaded.Window!.X);
        Assert.Equal(-171, reloaded.Window.Y);
        Assert.Equal(1200, reloaded.Window.Width);
        Assert.Equal(2000, reloaded.Window.Height);
    }

    /// <summary>
    /// 최대화 상태의 좌표가 아니라 <b>복원 크기</b>가 저장돼야 한다 — 뷰가 그렇게 넘긴다.
    /// 여기서는 그 값이 그대로 살아남는지만 본다.
    /// </summary>
    [Fact]
    public void MaximizingKeepsTheRestoreBoundsAlongsideTheFlag()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: true);
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        Assert.True(reloaded.Window!.Maximized);
        Assert.Equal(300, reloaded.Window.X);
        Assert.Equal(1400, reloaded.Window.Width);
    }

    /// <summary>
    /// 창을 끄는 동안 이벤트가 수십 번 온다. 같은 값이면 저장을 예약하지 않는다 —
    /// 안 그러면 아무것도 안 바뀌었는데 디스크를 계속 두드린다.
    /// </summary>
    [Fact]
    public void RepeatingTheSamePlacementDoesNotScheduleAnotherSave()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: false);
        f.Store.Flush();

        // 파일을 치워 두고 같은 값을 다시 넣는다 — 저장이 예약됐다면 파일이 되살아난다.
        var path = f.Paths.WorkspaceFile(f.Workspace.Id);
        File.Delete(path);

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: false);
        f.Store.Flush();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void OnlyTheMaximizedFlagChangingIsStillASave()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: false);
        f.Store.Flush();

        var path = f.Paths.WorkspaceFile(f.Workspace.Id);
        File.Delete(path);

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: true);
        f.Store.Flush();

        Assert.True(File.Exists(path));
        Assert.True(f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow().Window!.Maximized);
    }

    /// <summary>
    /// 최소화된 창의 <c>Left/Top</c> 은 -32000 같은 값이고 크기는 0 이 될 수 있다.
    /// 쓸 수 없는 사각형은 저장하지 않는다 — 그걸 저장하면 다음 실행에서 방어가 발동한다.
    /// </summary>
    [Theory]
    [InlineData(0, 900)]
    [InlineData(1400, 0)]
    [InlineData(double.NaN, 900)]
    public void AnUnusableRectangleIsIgnored(double width, double height)
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(100, 100, width, height), maximized: false);

        Assert.Null(f.Workspace.Window);
    }

    /// <summary>
    /// <c>window</c> 슬롯이 없던 구파일도 창을 한 번 옮기면 슬롯이 생기고 v5 로 승격된다.
    /// </summary>
    [Fact]
    public void AWorkspaceWithoutAWindowSlotGetsOneOnFirstMove()
    {
        using var f = new MainWindowFixture();

        Assert.Null(f.Workspace.Window);

        f.ViewModel.UpdateWindowPlacement(Rect(10, 20, 800, 600), maximized: false);
        f.Store.Flush();

        var json = File.ReadAllText(f.Paths.WorkspaceFile(f.Workspace.Id));
        Assert.Contains("\"window\"", json);
        Assert.Contains($"\"schemaVersion\": {Workspace.CurrentSchemaVersion}", json);
    }

    /// <summary>
    /// 방어가 발동했으면 조용히 넘기지 않는다 (§5). 실패는 아니므로 <b>정보</b> 등급 —
    /// 보조색이고 몇 초 뒤 사라진다 (§5.1).
    /// </summary>
    [Fact]
    public void FallingBackToTheDefaultPlaceIsAnInformationalMessage()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ReportWindowMoved();

        Assert.True(f.ViewModel.HasMessage);
        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.True(f.ViewModel.MessageIsTransient);
    }

    [Fact]
    public void ShrinkingToFitIsAlsoAnInformationalMessage()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ReportWindowResized();

        Assert.True(f.ViewModel.HasMessage);
        Assert.False(f.ViewModel.MessageIsWarning);
        Assert.True(f.ViewModel.MessageIsTransient);
    }

    /// <summary>
    /// 런처 창 위치는 저장하지 않는다 (§3.2 범위). 런처 뷰모델에 창 위치를 받는 창구가
    /// 없다는 것으로 못박는다 — 생기면 저장할 자리가 <c>settings.json</c> 이 되고,
    /// 그 파일의 프로세스 간 경합은 Stage 1 이 한계로 남긴 것이다.
    /// </summary>
    [Fact]
    public void TheLauncherDoesNotRememberItsOwnWindowPosition()
    {
        Assert.Null(typeof(LauncherViewModel).GetMethod(nameof(MainViewModel.UpdateWindowPlacement)));
        Assert.Null(typeof(Core.Models.AppSettings).GetProperty("Window"));
    }

    /// <summary>
    /// 실제 모니터 열거. 값은 PC 마다 다르므로 "말이 되는가"만 본다 —
    /// 최소 하나는 있고, 작업영역은 양수 크기이고, 주 모니터 번호가 범위 안이다.
    /// </summary>
    [Fact]
    public void TheMonitorSnapshotIsUsable()
    {
        var snapshot = MonitorLayout.Query();

        Assert.NotEmpty(snapshot.WorkAreas);
        Assert.InRange(snapshot.PrimaryIndex, 0, snapshot.WorkAreas.Count - 1);
        Assert.All(snapshot.WorkAreas, area => Assert.True(area.IsUsable));
        Assert.True(snapshot.Scale > 0);
    }

    /// <summary>
    /// 그리고 그 값으로 방어를 돌리면, 저장된 것이 없을 때 창이 실제 작업영역 안에 온다.
    /// 순수 규칙과 실제 모니터 값이 맞물리는 지점을 한 번은 밟아 둔다.
    /// </summary>
    [Fact]
    public void TheDefaultPlaceLandsInsideARealWorkArea()
    {
        var snapshot = MonitorLayout.Query();

        var placement = WindowPlacement.Resolve(null, snapshot.WorkAreas, snapshot.PrimaryIndex);

        Assert.Contains(snapshot.WorkAreas, area => WindowPlacement.CanBeGrabbed(placement.Bounds, area));
    }

    /// <summary>
    /// 뷰모델을 새로 만들어도(=다시 연 것과 같다) 저장된 창 위치는 손대지 않는다.
    /// 여는 쪽이 위치를 정하고, 뷰모델은 옮겼을 때만 쓴다.
    /// </summary>
    [Fact]
    public void OpeningAWorkspaceDoesNotRewriteTheSavedPosition()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: true);
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        _ = new MainViewModel(
            reloaded, f.Store, new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
            f.Engine, f.Prompt, f.MacroEditor, f.SelfLauncher);

        Assert.Equal(300, reloaded.Window!.X);
        Assert.True(reloaded.Window.Maximized);
    }
}
