using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

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

    [Fact]
    public void RepeatingTheSamePlacementDoesNotScheduleAnotherSave()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: false);
        f.Store.Flush();

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

    [Fact]
    public void TheLauncherDoesNotRememberItsOwnWindowPosition()
    {
        Assert.Null(typeof(LauncherViewModel).GetMethod(nameof(MainViewModel.UpdateWindowPlacement)));
        Assert.Null(typeof(Core.Models.AppSettings).GetProperty("Window"));
    }

    [Fact]
    public void TheMonitorSnapshotIsUsable()
    {
        var snapshot = MonitorLayout.Query();

        Assert.NotEmpty(snapshot.WorkAreas);
        Assert.InRange(snapshot.PrimaryIndex, 0, snapshot.WorkAreas.Count - 1);
        Assert.All(snapshot.WorkAreas, area => Assert.True(area.IsUsable));
        Assert.True(snapshot.Scale > 0);
    }

    [Fact]
    public void TheDefaultPlaceLandsInsideARealWorkArea()
    {
        var snapshot = MonitorLayout.Query();

        var placement = WindowPlacement.Resolve(null, snapshot.WorkAreas, snapshot.PrimaryIndex);

        Assert.Contains(snapshot.WorkAreas, area => WindowPlacement.CanBeGrabbed(placement.Bounds, area));
    }

    [Fact]
    public void OpeningAWorkspaceDoesNotRewriteTheSavedPosition()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.UpdateWindowPlacement(Rect(300, 200, 1400, 900), maximized: true);
        f.Store.Flush();

        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        _ = new MainViewModel(
            reloaded, f.Store, new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
            f.Engine, f.Prompt, f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        Assert.Equal(300, reloaded.Window!.X);
        Assert.True(reloaded.Window.Maximized);
    }
}
