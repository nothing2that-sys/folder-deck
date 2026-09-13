using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

/// <summary>타일 하나를 격자 전체 크기로 확장했다가 되돌리는 기능 (Phase 19 · D116).</summary>
public sealed class TileExpandTests
{
    [Fact]
    public void EffectivePlacementMatchesStoredValuesWhenNotExpanded()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        Assert.False(tile.IsExpanded);
        Assert.Equal(tile.CellX, tile.EffectiveCellX);
        Assert.Equal(tile.CellY, tile.EffectiveCellY);
        Assert.Equal(tile.SpanX, tile.EffectiveSpanX);
        Assert.Equal(tile.SpanY, tile.EffectiveSpanY);
    }

    [Fact]
    public void EffectivePlacementFillsTheGridWhenExpanded()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        f.ViewModel.ExpandedTile = tile;

        Assert.Equal(0, tile.EffectiveCellX);
        Assert.Equal(0, tile.EffectiveCellY);
        Assert.Equal(f.ViewModel.GridCols, tile.EffectiveSpanX);
        Assert.Equal(f.ViewModel.GridRows, tile.EffectiveSpanY);
    }

    [Fact]
    public void TogglingExpansionOnThenOffLeavesStoredPlacementUnchanged()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];
        var (cellX, cellY, spanX, spanY) = (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY);

        f.ViewModel.ExpandedTile = tile;
        f.ViewModel.ExpandedTile = null;

        Assert.Equal(cellX, tile.CellX);
        Assert.Equal(cellY, tile.CellY);
        Assert.Equal(spanX, tile.SpanX);
        Assert.Equal(spanY, tile.SpanY);
    }

    [Fact]
    public void ExpandingATileHidesEveryOtherTile()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];

        f.ViewModel.ExpandedTile = tile;

        Assert.True(tile.IsExpanded);
        Assert.False(tile.IsHiddenByExpansion);
        Assert.All(f.ViewModel.Tiles.Where(t => !ReferenceEquals(t, tile)), other =>
        {
            Assert.False(other.IsExpanded);
            Assert.True(other.IsHiddenByExpansion);
        });
    }

    [Fact]
    public void ClearingExpandedTileLowersBothFlagsOnEveryTile()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];
        f.ViewModel.ExpandedTile = tile;

        // 끄기 전에 실제로 켜져 있었는지부터 확인한다 — 안 그러면 "밀어넣기가 아예 없어서
        // 늘 꺼진 채"인 경우와 "제대로 껐다"인 경우를 이 시험이 구분하지 못한다(R2 로 실측).
        Assert.True(tile.IsExpanded);
        Assert.Contains(f.ViewModel.Tiles, t => t.IsHiddenByExpansion);

        f.ViewModel.ExpandedTile = null;

        Assert.All(f.ViewModel.Tiles, t =>
        {
            Assert.False(t.IsExpanded);
            Assert.False(t.IsHiddenByExpansion);
        });
    }

    [Fact]
    public void LayoutEditCannotBeToggledWhileATileIsExpanded()
    {
        using var f = new MainWindowFixture();
        Assert.True(f.ViewModel.ToggleLayoutEditCommand.CanExecute(null));

        f.ViewModel.ExpandedTile = f.ViewModel.Tiles[0];

        Assert.False(f.ViewModel.ToggleLayoutEditCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShowingAFolderInAnotherTileCollapsesTheExpandedTile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        // "code" 상시 칸을 확장한 채로, 순환 칸을 겨냥하는 "문서"를 클릭한다.
        f.ViewModel.ExpandedTile = f.ViewModel.Tiles[0];

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Null(f.ViewModel.ExpandedTile);
    }

    [Fact]
    public async Task ShowingAFolderInTheExpandedTileItselfKeepsExpansion()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("문서")); // 순환 칸이 "문서"를 담는다.

        var rotatingTile = f.ViewModel.Tiles.Single(t => ReferenceEquals(t.Panel, f.Rotating));
        f.ViewModel.ExpandedTile = rotatingTile;

        // 같은 순환 칸을 다시 겨냥하는 재클릭.
        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Same(rotatingTile, f.ViewModel.ExpandedTile);
    }

    /// <summary>
    /// T9(§3) — 지시서는 이 시험을 <c>WorkspaceRoundTripTests</c>(tests/FolderDeck.Core.Tests)
    /// 가 사는 자리에 얹으라고 적었으나, 그 프로젝트는 <c>FolderDeck.App</c> 을 참조하지 않는다
    /// (<c>FolderDeck.Core.Tests.csproj</c> 의 <c>ProjectReference</c> 는 <c>FolderDeck.Core</c>
    /// 하나뿐 — §4-1 조사 중 확인). <see cref="MainViewModel.ExpandedTile"/> 을 실제로 켜야
    /// 의미 있는 시험이 되므로(그래야 구현 전엔 컴파일조차 안 돼 "빨갛다"), App 층 뷰모델에
    /// 접근할 수 있는 이 파일에 대신 둔다. 파일 경로는 보고문에도 적는다.
    /// </summary>
    [Fact]
    public void SerializingTheWorkspaceIsByteIdenticalWhetherOrNotATileIsExpanded()
    {
        using var f = new MainWindowFixture();
        var tile = f.ViewModel.Tiles[0];
        var file = f.Paths.WorkspaceFile(f.Workspace.Id);

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        var before = File.ReadAllText(file);

        f.ViewModel.ExpandedTile = tile;

        Assert.Null(f.Store.SaveWorkspace(f.Workspace));
        var after = File.ReadAllText(file);

        Assert.Equal(before, after);
        Assert.Equal(10, Workspace.CurrentSchemaVersion);
    }

    // ── T10~T12(D118 §3) — RemoveFolder 가 확장된 타일을 지울 때의 정리 ──────────

    [Fact]
    public void RemovingTheExpandedTilesFolderClearsExpandedTile()
    {
        using var f = new MainWindowFixture();
        var codeRow = f.Row("code");
        var tile = f.ViewModel.Tiles.Single(t => t.IsPinned && t.Spec.FolderId == codeRow.Entry.Id);
        f.ViewModel.ExpandedTile = tile;

        f.ViewModel.RemoveFolder(codeRow);

        Assert.Null(f.ViewModel.ExpandedTile);
    }

    [Fact]
    public void RemovingTheExpandedTilesFolderLowersTheHiddenFlagOnSurvivors()
    {
        using var f = new MainWindowFixture();
        var codeRow = f.Row("code");
        var tile = f.ViewModel.Tiles.Single(t => t.IsPinned && t.Spec.FolderId == codeRow.Entry.Id);
        f.ViewModel.ExpandedTile = tile;

        // 지우기 전 — 다른 타일들이 실제로 숨겨져 있었는지부터 확인한다. 안 그러면 "가드가
        // 지운 뒤 값을 내렸다"와 "애초에 한 번도 안 켜졌다"를 이 시험이 구분하지 못한다
        // (ClearingExpandedTileLowersBothFlagsOnEveryTile 의 같은 주석 · D116 R2 와 같은 이유).
        var survivors = f.ViewModel.Tiles.Where(t => !ReferenceEquals(t, tile)).ToList();
        Assert.NotEmpty(survivors);
        Assert.All(survivors, t => Assert.True(t.IsHiddenByExpansion));

        f.ViewModel.RemoveFolder(codeRow);

        Assert.All(f.ViewModel.Tiles, t => Assert.False(t.IsHiddenByExpansion));
    }

    [Fact]
    public void RemovingADifferentFoldersRegistrationLeavesExpansionAlone()
    {
        using var f = new MainWindowFixture();
        var codeRow = f.Row("code");
        var outputRow = f.Row("산출물");
        var tile = f.ViewModel.Tiles.Single(t => t.IsPinned && t.Spec.FolderId == codeRow.Entry.Id);
        f.ViewModel.ExpandedTile = tile;

        f.ViewModel.RemoveFolder(outputRow);

        Assert.Same(tile, f.ViewModel.ExpandedTile);
        Assert.True(tile.IsExpanded);
    }
}
