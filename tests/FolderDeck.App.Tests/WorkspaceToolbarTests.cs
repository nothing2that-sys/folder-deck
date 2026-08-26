using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;

/// <summary>
/// 워크스페이스 창의 제목줄과 툴바 (Phase 8-B ⑤ · ⑧).
/// </summary>
public sealed class WorkspaceToolbarTests
{
    // ── ⑤ 창 제목 ────────────────────────────────────────────────────────────

    [Fact]
    public void TheWindowTitleCarriesTheDescription()
    {
        using var f = new MainWindowFixture();
        f.Workspace.Description = "라인 3 검사 설비";

        Assert.Equal("FolderDeck — 테스트 작업 · 라인 3 검사 설비", f.ViewModel.WindowTitle);
    }

    /// <summary>설명이 없으면 <b>구분점째로</b> 빠진다 — 뜻 없는 점이 제목줄에 남지 않는다.</summary>
    [Fact]
    public void WithoutADescriptionOnlyTheTitleIsThere()
    {
        using var f = new MainWindowFixture();
        Assert.Null(f.Workspace.Description);

        Assert.Equal("FolderDeck — 테스트 작업", f.ViewModel.WindowTitle);
    }

    /// <summary>공백뿐인 설명도 없는 것으로 본다.</summary>
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

    // ── ⑧ 툴바의 순환 폴더 줄 ────────────────────────────────────────────────

    /// <summary>
    /// 상시 칸에 못박힌 폴더는 빠진다 — 눌러도 순환 칸이 아니라 자기 상시 칸이 반응하므로
    /// (<see cref="MainViewModel.ShowFolderAsync"/>) 순환 줄에 세워 두면 거짓말이 된다.
    /// </summary>
    [Fact]
    public void OnlyFoldersThatActuallyGoToARotatingTileAreListed()
    {
        using var f = new MainWindowFixture();

        Assert.Equal(
            ["작업", "문서", "설비 로그"],
            f.ViewModel.RotatingRows.Select(r => r.DisplayName));

        // 왼쪽 목록의 pin 글리프와 같은 값으로 걸렀다.
        Assert.All(f.ViewModel.RotatingRows, r => Assert.False(r.HasPinnedTile));
    }

    /// <summary><b>같은 객체</b>다 — 새 상태를 만들지 않았다.</summary>
    [Fact]
    public void TheRowsAreTheSameObjectsAsTheLeftList()
    {
        using var f = new MainWindowFixture();

        foreach (var row in f.ViewModel.RotatingRows)
        {
            Assert.Contains(f.ViewModel.Rows, r => ReferenceEquals(r, row));
        }
    }

    /// <summary>
    /// 툴바 타일은 왼쪽 목록 클릭과 <b>같은 커맨드·같은 인자</b>로 간다 — 여기서 확인하는 것은
    /// 그 커맨드를 태웠을 때의 결과가 왼쪽 목록을 눌렀을 때와 같다는 것이다.
    /// </summary>
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

    /// <summary>폴더 등록을 지우면 줄에서도 빠진다.</summary>
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

    /// <summary>
    /// 안 바뀌었으면 컬렉션을 손대지 않는다 — 매번 다시 채우면 손이 올라가 있던 버튼이
    /// 사라졌다 나타난다.
    /// </summary>
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
