using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

/// <summary>1회용 창 배치 프리셋 (2026-10-14 S2).</summary>
public sealed class LayoutPresetsTests
{
    public static TheoryData<LayoutPreset, (int X, int Y, int W, int H)[]> Expected => new()
    {
        { LayoutPreset.Single, [(0, 0, 4, 4)] },
        { LayoutPreset.SideBySide, [(0, 0, 2, 4), (2, 0, 2, 4)] },
        { LayoutPreset.Stacked, [(0, 0, 4, 2), (0, 2, 4, 2)] },
        { LayoutPreset.Quad, [(0, 0, 2, 2), (2, 0, 2, 2), (0, 2, 2, 2), (2, 2, 2, 2)] },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void EachPresetBuildsTheTableCellsInReadingOrder(LayoutPreset preset, (int X, int Y, int W, int H)[] cells)
    {
        var tiles = LayoutPresets.Build(preset);

        Assert.Equal(cells.Length, tiles.Count);
        for (var i = 0; i < cells.Length; i++)
        {
            Assert.Equal(cells[i], (tiles[i].CellX, tiles[i].CellY, tiles[i].SpanX, tiles[i].SpanY));
            Assert.Equal(TileKind.Rotating, tiles[i].Kind);
            Assert.Null(tiles[i].FolderId);
            Assert.Equal(i + 1, tiles[i].RotationIndex);
        }
    }

    [Theory]
    [MemberData(nameof(Expected))]
    public void EveryTileIsInsideTheGridAndNoTwoOverlap(LayoutPreset preset, (int X, int Y, int W, int H)[] _)
    {
        var tiles = LayoutPresets.Build(preset);

        Assert.All(tiles, t => Assert.True(GridLayout.IsInside(
            t.CellX, t.CellY, t.SpanX, t.SpanY, GridLayout.DefaultCols, GridLayout.DefaultRows)));

        for (var i = 0; i < tiles.Count; i++)
        {
            for (var j = 0; j < tiles.Count; j++)
            {
                if (i != j)
                {
                    var o = tiles[j];
                    Assert.False(GridLayout.Overlaps(tiles[i], o.CellX, o.CellY, o.SpanX, o.SpanY));
                }
            }
        }
    }

    /// <summary>
    /// 「빈 칸은 순환 칸이어야 한다」는 결정을 실물로 잰다 — 상시 칸으로 만들었다면
    /// <see cref="GridLayout.NormalizeTiles"/> 가 폴더 없는 칸을 지워 1회용 창이 빈 화면으로 뜬다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Expected))]
    public void NormalizingAnEmptyWorkspaceDropsNoPresetTile(LayoutPreset preset, (int X, int Y, int W, int H)[] cells)
    {
        var workspace = new Workspace { Tiles = LayoutPresets.Build(preset) };

        Assert.Equal(0, GridLayout.NormalizeTiles(workspace));
        Assert.Equal(cells.Length, workspace.Tiles!.Count);
    }

    [Fact]
    public void EveryPresetHasALabel() =>
        Assert.All(LayoutPresets.All, p => Assert.False(string.IsNullOrWhiteSpace(LayoutPresets.Label(p))));
}
