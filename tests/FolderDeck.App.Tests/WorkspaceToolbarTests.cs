using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;

public sealed class WorkspaceToolbarTests
{

    [Fact]
    public void TheWindowTitleCarriesTheDescription()
    {
        using var f = new MainWindowFixture();
        f.Workspace.Description = "라인 3 검사 설비";

        Assert.Equal("FolderDeck — 테스트 작업 · 라인 3 검사 설비", f.ViewModel.WindowTitle);
    }

    [Fact]
    public void WithoutADescriptionOnlyTheTitleIsThere()
    {
        using var f = new MainWindowFixture();
        Assert.Null(f.Workspace.Description);

        Assert.Equal("FolderDeck — 테스트 작업", f.ViewModel.WindowTitle);
    }

    [Fact]
    public void ABlankDescriptionIsTreatedAsMissing()
    {
        using var f = new MainWindowFixture();
        f.Workspace.Description = "   ";

        Assert.Equal("FolderDeck — 테스트 작업", f.ViewModel.WindowTitle);
    }

    [Fact]
    public void AnUntitledWorkspaceStillReadsAsSomething()
    {
        using var f = new MainWindowFixture();
        f.Workspace.Title = string.Empty;
        f.Workspace.Description = "설명만 있다";

        Assert.Equal("FolderDeck — (제목 없음) · 설명만 있다", f.ViewModel.WindowTitle);
    }

    [Fact]
    public void OnlyFoldersThatActuallyGoToARotatingTileAreListed()
    {
        using var f = new MainWindowFixture();

        Assert.Equal(
            ["작업", "문서", "설비 로그"],
            f.ViewModel.RotatingRows.Select(r => r.DisplayName));

        Assert.All(f.ViewModel.RotatingRows, r => Assert.False(r.HasPinnedTile));
    }

    [Fact]
    public void TheRowsAreTheSameObjectsAsTheLeftList()
    {
        using var f = new MainWindowFixture();

        foreach (var row in f.ViewModel.RotatingRows)
        {
            Assert.Contains(f.ViewModel.Rows, r => ReferenceEquals(r, row));
        }
    }

    [Fact]
    public async Task ClickingATileLandsWhereTheLeftListWouldLand()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var row = f.ViewModel.RotatingRows.Single(r => r.DisplayName == "작업");

        await f.ViewModel.ShowFolderCommand.ExecuteAsync(row);

        Assert.Same(row.Entry, f.Rotating.Entry);
        Assert.True(row.IsShowingInRotating);
    }

    [Fact]
    public void RemovingAFolderTakesItOffTheToolbar()
    {
        using var f = new MainWindowFixture();

        var row = f.Row("문서");
        f.Prompt.Answer = true;
        f.ViewModel.RemoveFolderCommand.Execute(row);

        Assert.DoesNotContain(f.ViewModel.RotatingRows, r => ReferenceEquals(r, row));
        Assert.Equal(["작업", "설비 로그"], f.ViewModel.RotatingRows.Select(r => r.DisplayName));
    }

    [Fact]
    public async Task ARefreshThatChangesNothingLeavesTheCollectionAlone()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var changes = 0;
        f.ViewModel.RotatingRows.CollectionChanged += (_, _) => changes++;

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal(0, changes);
    }
}
