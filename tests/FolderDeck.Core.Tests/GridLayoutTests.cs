using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

public sealed class GridLayoutTests
{
    private static Workspace WorkspaceWith(params bool[] pinned) => new()
    {
        Title = "배치",
        Folders = [.. pinned.Select((p, i) => new FolderEntry { Path = $@"D:\work\f{i}", Pinned = p })],
    };

    [Fact]
    public void DerivedLayoutIsPinnedTilesPlusExactlyOneRotating()
    {
        var workspace = WorkspaceWith(true, true, false, false);

        Assert.True(GridLayout.EnsureTiles(workspace));

        var tiles = workspace.Tiles!;
        Assert.Equal(3, tiles.Count);
        Assert.Equal(2, tiles.Count(t => t.Kind == TileKind.Pinned));

        var rotating = Assert.Single(tiles, t => t.Kind == TileKind.Rotating);
        Assert.Equal(1, rotating.RotationIndex);
        Assert.Null(rotating.FolderId);

        Assert.Same(rotating, tiles[^1]);
    }

    [Fact]
    public void DerivedTilesNeitherOverlapNorLeaveTheGrid()
    {
        for (var pinnedCount = 0; pinnedCount <= 15; pinnedCount++)
        {
            var workspace = WorkspaceWith([.. Enumerable.Repeat(true, pinnedCount)]);
            GridLayout.EnsureTiles(workspace);

            var placed = new List<TileSpec>();
            foreach (var tile in workspace.Tiles!)
            {
                Assert.True(
                    GridLayout.CanPlace(placed, null, tile.CellX, tile.CellY, tile.SpanX, tile.SpanY, 4, 4),
                    $"상시 {pinnedCount}개에서 ({tile.CellX},{tile.CellY}) {tile.SpanX}x{tile.SpanY} 가 겹치거나 밖으로 나갔다.");
                placed.Add(tile);
            }

            Assert.Equal(pinnedCount + 1, workspace.Tiles.Count);
        }
    }

    [Fact]
    public void DerivedLayoutNeverExceedsSixteenTiles()
    {
        var workspace = WorkspaceWith([.. Enumerable.Repeat(true, 30)]);

        GridLayout.EnsureTiles(workspace);

        Assert.Equal(16, workspace.Tiles!.Count);
        Assert.Single(workspace.Tiles, t => t.Kind == TileKind.Rotating);
        Assert.All(workspace.Tiles, t => Assert.Equal((1, 1), (t.SpanX, t.SpanY)));
    }

    [Fact]
    public void NoPinnedFoldersStillGetsOneRotatingTileFillingTheGrid()
    {
        var workspace = WorkspaceWith(false, false);

        GridLayout.EnsureTiles(workspace);

        var tile = Assert.Single(workspace.Tiles!);
        Assert.Equal(TileKind.Rotating, tile.Kind);
        Assert.Equal((0, 0, 4, 4), (tile.CellX, tile.CellY, tile.SpanX, tile.SpanY));
    }

    [Fact]
    public void ExistingTilesAreNotRederived()
    {
        var workspace = WorkspaceWith(true, true);
        workspace.Tiles = [new TileSpec { Kind = TileKind.Rotating, RotationIndex = 1 }];

        Assert.False(GridLayout.EnsureTiles(workspace));
        Assert.Single(workspace.Tiles);
    }

    [Fact]
    public void GridIsAlwaysFourByFour()
    {
        var workspace = WorkspaceWith();

        var grid = GridLayout.EnsureGrid(workspace);

        Assert.Equal(4, grid.Cols);
        Assert.Equal(4, grid.Rows);
        Assert.Same(grid, workspace.Grid);
    }

    [Theory]
    [InlineData(0, 0, 4, 4, true)]
    [InlineData(3, 3, 1, 1, true)]
    [InlineData(3, 0, 2, 1, false)]
    [InlineData(0, 3, 1, 2, false)]
    [InlineData(-1, 0, 1, 1, false)]
    [InlineData(0, 0, 0, 1, false)]
    public void IsInsideGuardsTheGridEdges(int x, int y, int spanX, int spanY, bool expected) =>
        Assert.Equal(expected, GridLayout.IsInside(x, y, spanX, spanY, 4, 4));

    [Fact]
    public void OverlappingPlacementIsRejectedNotPushedAside()
    {
        var sitting = new TileSpec { CellX = 1, CellY = 1, SpanX = 2, SpanY = 2 };
        List<TileSpec> tiles = [sitting];

        Assert.False(GridLayout.CanPlace(tiles, null, 2, 2, 2, 2, 4, 4));
        Assert.False(GridLayout.CanPlace(tiles, null, 0, 0, 2, 2, 4, 4));

        Assert.True(GridLayout.CanPlace(tiles, null, 3, 1, 1, 2, 4, 4));
        Assert.True(GridLayout.CanPlace(tiles, null, 0, 0, 1, 4, 4, 4));

        Assert.True(GridLayout.CanPlace(tiles, sitting, 1, 1, 3, 3, 4, 4));
    }

    [Fact]
    public void FreeCellsAreTheOnesNoTileCovers()
    {
        List<TileSpec> tiles = [new() { CellX = 0, CellY = 0, SpanX = 3, SpanY = 4 }];

        var free = GridLayout.FreeCells(tiles, 4, 4);

        Assert.Equal(4, free.Count);
        Assert.All(free, cell => Assert.Equal(3, cell.X));
    }

    [Fact]
    public void NextRotationIndexTakesTheLowestFreeNumber()
    {
        Assert.Equal(1, GridLayout.NextRotationIndex([]));

        List<TileSpec> tiles =
        [
            new() { Kind = TileKind.Rotating, RotationIndex = 1 },
            new() { Kind = TileKind.Pinned },
        ];
        Assert.Equal(2, GridLayout.NextRotationIndex(tiles));

        tiles.Add(new TileSpec { Kind = TileKind.Rotating, RotationIndex = 2 });
        Assert.Equal(3, GridLayout.NextRotationIndex(tiles));

        tiles.RemoveAt(0);
        Assert.Equal(1, GridLayout.NextRotationIndex(tiles));
    }

    [Fact]
    public void NormalizePullsOversizedTilesBackInsideTheGrid()
    {
        var workspace = WorkspaceWith(true);
        var folderId = workspace.Folders[0].Id;
        workspace.Tiles =
        [
            new TileSpec { Kind = TileKind.Pinned, FolderId = folderId, CellX = 3, CellY = 3, SpanX = 3, SpanY = 3 },
        ];

        Assert.Equal(0, GridLayout.NormalizeTiles(workspace));

        var tile = Assert.Single(workspace.Tiles);
        Assert.True(GridLayout.IsInside(tile.CellX, tile.CellY, tile.SpanX, tile.SpanY, 4, 4));
    }

    [Fact]
    public void NormalizeDropsTilesWhoseFolderIsGone()
    {
        var workspace = WorkspaceWith(true);
        workspace.Tiles =
        [
            new TileSpec { Kind = TileKind.Pinned, FolderId = Guid.NewGuid() },
            new TileSpec { Kind = TileKind.Rotating, CellX = 1, RotationIndex = 1 },
        ];

        Assert.Equal(1, GridLayout.NormalizeTiles(workspace));
        Assert.Equal(TileKind.Rotating, Assert.Single(workspace.Tiles).Kind);
    }

    [Fact]
    public void NormalizeDropsOverlappingTilesAndRenumbersDuplicateRotations()
    {
        var workspace = WorkspaceWith();
        workspace.Tiles =
        [
            new TileSpec { Kind = TileKind.Rotating, CellX = 0, CellY = 0, SpanX = 2, SpanY = 2, RotationIndex = 1 },
            new TileSpec { Kind = TileKind.Rotating, CellX = 1, CellY = 1, RotationIndex = 5 },
            new TileSpec { Kind = TileKind.Rotating, CellX = 3, CellY = 3, RotationIndex = 1 },
        ];

        Assert.Equal(1, GridLayout.NormalizeTiles(workspace));

        Assert.Equal(2, workspace.Tiles.Count);
        Assert.Equal([1, 2], workspace.Tiles.Select(t => t.RotationIndex));
    }
}
