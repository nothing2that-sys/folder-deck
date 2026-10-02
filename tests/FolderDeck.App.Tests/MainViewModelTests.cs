using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;


public sealed class MainViewModelTests
{
    [Fact]
    public void LeftListShowsEveryFolderWithPinAndDescription()
    {
        using var f = new MainWindowFixture();

        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Equal(
            ["code", "산출물", "작업", "문서", "설비 로그"],
            f.ViewModel.Rows.Select(r => r.DisplayName));

        Assert.True(f.Row("code").Pinned);
        Assert.True(f.Row("산출물").Pinned);
        Assert.False(f.Row("문서").Pinned);

        Assert.Equal("구현 코드", f.Row("code").Description);
        Assert.True(f.Row("code").HasDescription);
    }


    [Fact]
    public void PanelsAreDerivedFromPinnedPlusOneRotating()
    {
        using var f = new MainWindowFixture();


        Assert.Equal(3, f.ViewModel.Panels.Count);
        Assert.Equal(2, f.ViewModel.Panels.Count(p => !p.IsRotating));
        Assert.Single(f.ViewModel.Panels, p => p.IsRotating);


        Assert.True(f.ViewModel.Panels[^1].IsRotating);
        Assert.Same(f.Rotating, f.ViewModel.Panels[^1]);


        Assert.Equal([f.CodePath, f.OutputPath], f.ViewModel.Panels.Where(p => !p.IsRotating).Select(p => p.Entry!.Path));
        Assert.True(f.Rotating.IsEmpty);
    }


    [Fact]
    public void TilesArrayIsFilledInAndBecomesTheSourceOfTruth()
    {
        using var f = new MainWindowFixture();

        Assert.NotNull(f.Workspace.Tiles);
        Assert.Equal(3, f.Workspace.Tiles!.Count);
        Assert.Equal(4, f.Workspace.Grid!.Cols);


        Assert.Equal(f.Workspace.Tiles, f.ViewModel.Tiles.Select(t => t.Spec));


        Assert.All(f.ViewModel.Tiles, t => Assert.Equal((2, 2), (t.SpanX, t.SpanY)));
        Assert.Equal([(0, 0), (2, 0), (0, 2)], f.ViewModel.Tiles.Select(t => (t.CellX, t.CellY)));


        Assert.Equal(4, f.ViewModel.Cells.Count(c => c.IsFree));
    }

    [Fact]
    public async Task InitializeLoadsPinnedPanelsFromTheirAnchors()
    {
        using var f = new MainWindowFixture();

        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(f.CodePath, code.CurrentPath);
        Assert.False(code.IsAwayFromAnchor);
        Assert.False(code.HasFailure);


        Assert.Equal(["Recipe", "Views", "Main.cs"], code.Items.Select(i => i.Name));
        Assert.True(code.Items[0].IsDirectory);
    }

    [Fact]
    public async Task ClickingARowShowsThatFolderInTheRotatingPanel()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var rotating = f.Rotating;
        Assert.Equal(f.DocsPath, rotating.CurrentPath);
        Assert.Equal("spec.md", Assert.Single(rotating.Items).Name);
        Assert.True(f.Row("문서").IsShowingInRotating);
    }





    [Fact]
    public async Task ClickingAPinnedRowResetsItsOwnTileAndLeavesTheRotatingCellAlone()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var pinned = f.ViewModel.Panels[0];
        await pinned.OpenAsync(pinned.Items.Single(i => i.Name == "Recipe"));
        Assert.True(pinned.IsAwayFromAnchor);

        await f.ViewModel.ShowFolderAsync(f.Row("code"));


        Assert.Equal(f.CodePath, pinned.CurrentPath);
        Assert.False(pinned.IsAwayFromAnchor);
        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
        Assert.False(f.Row("code").IsShowingInRotating);


        await f.ViewModel.ShowFolderAsync(f.Row("code"));
        Assert.Contains("상시 칸에 이미 떠 있다", f.ViewModel.Message);
    }

    [Fact]
    public async Task ClickingAnotherRowReplacesWhatTheRotatingPanelShows()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        Assert.Equal(f.WorkPath, f.Rotating.CurrentPath);
        Assert.False(f.Row("문서").IsShowingInRotating);
        Assert.True(f.Row("작업").IsShowingInRotating);
    }


    [Fact]
    public async Task StatusBarFollowsTheRotatingPanel()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.DoesNotContain(@"\", f.ViewModel.StatusPath);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.Equal(f.WorkPath, f.ViewModel.StatusPath);

        var recipe = f.Rotating.Items.Single(i => i.Name == "Recipe");
        await f.Rotating.OpenAsync(recipe);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.ViewModel.StatusPath);
    }


    [Fact]
    public async Task ViewModeAndSortComeFromTheSavedEntry()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.IsDetailsView);
        Assert.False(code.IsListView);
        Assert.Contains("details", code.MetaText);
        Assert.Contains("이름 ↑", code.MetaText);


        var output = f.ViewModel.Panels[1];
        Assert.Contains("크기 ↓", output.MetaText);
        Assert.Equal(["b.dll", "a.dll"], output.Items.Select(i => i.Name));


        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.True(f.Rotating.IsListView);
        Assert.False(f.Rotating.IsDetailsView);
        Assert.Contains("list", f.Rotating.MetaText);
    }



    [Fact]
    public async Task UnreachableFolderRendersAsInaccessibleAndStaysInTheList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.True(f.Row("설비 로그").IsInaccessible);

        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        var rotating = f.Rotating;
        Assert.True(rotating.HasFailure);
        Assert.Empty(rotating.Items);
        Assert.Contains("접근 불가", rotating.FailureText);


        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Contains(MainWindowFixture.UnreachablePath, f.ViewModel.Rows.Select(r => r.PathText));
        Assert.True(f.Row("설비 로그").IsInaccessible);
    }


    [Fact]
    public async Task RetryReEnumeratesAnInaccessibleFolder()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));
        Assert.True(f.Rotating.HasFailure);

        await f.Rotating.RefreshAsync();


        Assert.True(f.Rotating.HasFailure);
        Assert.Equal(MainWindowFixture.UnreachablePath, f.Rotating.CurrentPath);
    }

    [Fact]
    public async Task AReachableFolderIsNotMarkedInaccessible()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.False(f.Row("code").IsInaccessible);
        Assert.False(f.Row("문서").IsInaccessible);
    }








    [Fact]
    public async Task ARecoveredFolderClearsInaccessibleOnItsOwnRefresh()
    {
        using var f = new MainWindowFixture();
        f.Store.Flush();
        var reopened = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();

        var recoveringPath = Path.Combine(Path.GetDirectoryName(f.CodePath)!, "recovers");
        reopened.Folders.Add(new FolderEntry
        {
            Path = recoveringPath, DisplayName = "복구됨", Description = "시험용",
        });

        var viewModel = new MainViewModel(
            reopened, f.Store, new FolderEnumerator(), f.Shell, f.Clipboard, f.Engine, f.Prompt,
            f.MacroEditor, f.FolderEditor, f.SelfLauncher);

        await viewModel.InitializeAsync();

        var row = viewModel.Rows.Single(r => r.DisplayName == "복구됨");
        Assert.True(row.IsInaccessible);

        await viewModel.ShowFolderAsync(row);
        Assert.True(viewModel.RotatingPanel!.HasFailure);


        Directory.CreateDirectory(recoveringPath);
        await viewModel.RotatingPanel!.RefreshAsync();

        Assert.False(viewModel.RotatingPanel!.HasFailure);
        Assert.False(row.IsInaccessible);
    }



    [Fact]
    public async Task RefreshPicksUpAFileDroppedIntoTheFolder()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.Single(f.Rotating.Items);

        File.WriteAllText(Path.Combine(f.DocsPath, "새파일.txt"), "새 내용");


        await f.ViewModel.RefreshAllAsync();

        Assert.Equal(2, f.Rotating.Items.Count);
        Assert.Contains("새파일.txt", f.Rotating.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task RefreshAlsoUpdatesPinnedPanels()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var before = f.ViewModel.Panels[0].Items.Count;

        File.WriteAllText(Path.Combine(f.CodePath, "Added.cs"), "x");
        await f.ViewModel.RefreshAllAsync();

        Assert.Equal(before + 1, f.ViewModel.Panels[0].Items.Count);
    }

    [Fact]
    public async Task RefreshKeepsTheSubfolderYouNavigatedInto()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var recipe = f.Rotating.Items.Single(i => i.Name == "Recipe");
        await f.Rotating.OpenAsync(recipe);

        await f.ViewModel.RefreshAllAsync();

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.Rotating.CurrentPath);
    }








    [Fact]
    public async Task LoadCompletedFiresOncePerRead()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var panel = f.Rotating;
        var fired = 0;
        panel.LoadCompleted += (_, _) => fired++;

        await panel.RefreshAsync();

        Assert.Equal(1, fired);
        Assert.False(panel.HasFailure);
        Assert.Single(panel.Items);
    }





    [Fact]
    public async Task LoadCompletedFiresOnceEvenWhenTheReadFails()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("설비 로그"));

        var panel = f.Rotating;
        Assert.True(panel.HasFailure);

        var fired = 0;
        panel.LoadCompleted += (_, _) => fired++;


        await panel.RefreshAsync();

        Assert.Equal(1, fired);
        Assert.True(panel.HasFailure);
        Assert.Equal(MainWindowFixture.UnreachablePath, panel.CurrentPath);
    }



    [Fact]
    public async Task DoubleClickingAFileHandsItToTheShell()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        var file = f.Rotating.Items.Single(i => i.Name == "spec.md");
        await f.Rotating.OpenAsync(file);

        Assert.Equal(Path.Combine(f.DocsPath, "spec.md"), Assert.Single(f.Shell.Opened));


        Assert.Equal(f.DocsPath, f.Rotating.CurrentPath);
    }

    [Fact]
    public async Task DoubleClickingAFolderNavigatesInsteadOfShellingOut()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));

        var recipe = f.Rotating.Items.Single(i => i.Name == "Recipe");
        await f.Rotating.OpenAsync(recipe);

        Assert.Empty(f.Shell.Opened);
        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), f.Rotating.CurrentPath);
    }

    [Fact]
    public async Task ShellFailureIsSurfacedNotSwallowed()
    {
        using var f = new MainWindowFixture();
        f.Shell.Error = "연결된 프로그램이 없다";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        await f.Rotating.OpenAsync(
            f.Rotating.Items.Single(i => i.Name == "spec.md"));

        Assert.True(f.ViewModel.HasMessage);
        Assert.Contains("연결된 프로그램이 없다", f.ViewModel.Message);
    }




    [Fact]
    public void OpenLauncherStartsTheLauncher()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.OpenLauncherCommand.Execute(null);

        Assert.Equal(1, f.SelfLauncher.LauncherCount);
        Assert.False(f.ViewModel.MessageIsWarning);
    }

    [Fact]
    public void FailingToStartTheLauncherIsSurfacedAndStays()
    {
        using var f = new MainWindowFixture();
        f.SelfLauncher.Error = "실행 파일 경로를 알 수 없다";

        f.ViewModel.OpenLauncherCommand.Execute(null);

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("실행 파일 경로를 알 수 없다", f.ViewModel.Message);
    }





    [Fact]
    public void ADuplicateWindowWarningStaysAndSaysWhy()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ReportDuplicateWindow();

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("두 창", f.ViewModel.Message);


        f.ViewModel.ExpireTransientMessage();
        Assert.True(f.ViewModel.HasMessage);
    }

    [Fact]
    public void AWindowThatCannotReceiveSignalsSaysSoAndStays()
    {
        using var f = new MainWindowFixture();

        f.ViewModel.ReportNoSignal("모든 파이프 인스턴스가 사용 중입니다.");

        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.False(f.ViewModel.MessageIsTransient);
        Assert.Contains("모든 파이프 인스턴스가 사용 중입니다.", f.ViewModel.Message);
    }












    private static List<(bool Warning, bool Transient)> FlagsWhenMessageFires(MainViewModel vm)
    {
        var seen = new List<(bool Warning, bool Transient)>();

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Message))
            {
                seen.Add((vm.MessageIsWarning, vm.MessageIsTransient));
            }
        };

        return seen;
    }






    [Fact]
    public void TheFirstTransientIsAlreadyTransientWhenTheMessageNotificationFires()
    {
        using var f = new MainWindowFixture();

        var seen = FlagsWhenMessageFires(f.ViewModel);

        f.ViewModel.ReportTileOverlap();

        var (warning, transient) = Assert.Single(seen);
        Assert.True(transient);
        Assert.True(warning);
    }


    [Fact]
    public void AStayingWarningDoesNotInheritTheTransientFlagOfTheOneBefore()
    {
        using var f = new MainWindowFixture();
        f.ViewModel.ReportTileOverlap();

        var seen = FlagsWhenMessageFires(f.ViewModel);

        f.ViewModel.ReportDuplicateWindow();

        var (warning, transient) = Assert.Single(seen);
        Assert.False(transient);
        Assert.True(warning);
    }


    [Fact]
    public void ClearingLowersBothFlagsBeforeTheMessageNotification()
    {
        using var f = new MainWindowFixture();
        f.ViewModel.ReportTileOverlap();

        var seen = FlagsWhenMessageFires(f.ViewModel);

        f.ViewModel.ExpireTransientMessage();

        var (warning, transient) = Assert.Single(seen);
        Assert.False(transient);
        Assert.False(warning);
    }








    [Fact]
    public async Task RefreshKeepsAWarningThatIsMeantToStay()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.ReportDuplicateWindow();
        Assert.False(f.ViewModel.MessageIsTransient);

        await f.ViewModel.RefreshAllAsync();

        Assert.True(f.ViewModel.HasMessage);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("두 창", f.ViewModel.Message);
    }


    [Fact]
    public async Task RefreshStillClearsATransientRefusal()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.ReportTileOverlap();
        Assert.True(f.ViewModel.MessageIsTransient);

        await f.ViewModel.RefreshAllAsync();

        Assert.False(f.ViewModel.HasMessage);
    }
}
