using FolderDeck.Core.Models;

namespace FolderDeck.Core.Layout;

public static class GridLayout
{
    public const int DefaultCols = 4;
    public const int DefaultRows = 4;

    public static GridSpec EnsureGrid(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var grid = workspace.Grid ??= new GridSpec();
        grid.Cols = DefaultCols;
        grid.Rows = DefaultRows;
        return grid;
    }

    public static bool EnsureTiles(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var grid = EnsureGrid(workspace);
        if (workspace.Tiles is { Count: > 0 })
        {
            return false;
        }

        workspace.Tiles = DeriveFromPinned(workspace.Folders, grid.Cols, grid.Rows);
        return true;
    }

    public static List<TileSpec> DeriveFromPinned(IReadOnlyList<FolderEntry> folders, int cols, int rows)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var capacity = cols * rows;
        var pinned = folders.Where(f => f.Pinned).ToList();

        var count = Math.Min(pinned.Count + 1, capacity);
        var pinnedCount = count - 1;

        var gridCols = Math.Min((int)Math.Ceiling(Math.Sqrt(count)), cols);
        var gridRows = Math.Min((int)Math.Ceiling(count / (double)gridCols), rows);

        var widths = Distribute(cols, gridCols);
        var heights = Distribute(rows, gridRows);
        var xs = Offsets(widths);
        var ys = Offsets(heights);

        var tiles = new List<TileSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var gc = i % gridCols;
            var gr = i / gridCols;

            var tile = new TileSpec
            {
                CellX = xs[gc],
                CellY = ys[gr],
                SpanX = widths[gc],
                SpanY = heights[gr],
            };

            if (i < pinnedCount)
            {
                tile.Kind = TileKind.Pinned;
                tile.FolderId = pinned[i].Id;
            }
            else
            {
                tile.Kind = TileKind.Rotating;
                tile.RotationIndex = 1;
            }

            tiles.Add(tile);
        }

        return tiles;
    }

    public static int NormalizeTiles(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var grid = EnsureGrid(workspace);
        var tiles = workspace.Tiles;
        if (tiles is null)
        {
            return 0;
        }

        var known = workspace.Folders.Select(f => f.Id).ToHashSet();
        var kept = new List<TileSpec>(tiles.Count);
        var dropped = 0;

        foreach (var tile in tiles)
        {

            if (tile.Kind == TileKind.Pinned && (tile.FolderId is null || !known.Contains(tile.FolderId.Value)))
            {
                dropped++;
                continue;
            }

            if (tile.Kind == TileKind.Rotating)
            {
                tile.FolderId = null;
                if (tile.RotationIndex is null or < 1)
                {
                    tile.RotationIndex = NextRotationIndex(kept);
                }
            }
            else
            {
                tile.RotationIndex = null;
            }

            tile.SpanX = Math.Clamp(tile.SpanX, 1, grid.Cols);
            tile.SpanY = Math.Clamp(tile.SpanY, 1, grid.Rows);
            tile.CellX = Math.Clamp(tile.CellX, 0, grid.Cols - tile.SpanX);
            tile.CellY = Math.Clamp(tile.CellY, 0, grid.Rows - tile.SpanY);

            if (!CanPlace(kept, null, tile.CellX, tile.CellY, tile.SpanX, tile.SpanY, grid.Cols, grid.Rows))
            {
                dropped++;
                continue;
            }

            kept.Add(tile);
        }

        var seen = new HashSet<int>();
        foreach (var tile in kept.Where(t => t.Kind == TileKind.Rotating))
        {
            if (!seen.Add(tile.RotationIndex!.Value))
            {
                tile.RotationIndex = NextRotationIndex(kept);
                seen.Add(tile.RotationIndex.Value);
            }
        }

        workspace.Tiles = kept;
        return dropped;
    }

    public static bool IsInside(int cellX, int cellY, int spanX, int spanY, int cols, int rows) =>
        spanX >= 1 && spanY >= 1
        && cellX >= 0 && cellY >= 0
        && cellX + spanX <= cols
        && cellY + spanY <= rows;

    public static bool Overlaps(TileSpec tile, int cellX, int cellY, int spanX, int spanY)
    {
        ArgumentNullException.ThrowIfNull(tile);

        return cellX < tile.CellX + tile.SpanX
               && tile.CellX < cellX + spanX
               && cellY < tile.CellY + tile.SpanY
               && tile.CellY < cellY + spanY;
    }

    public static bool CanPlace(
        IEnumerable<TileSpec> tiles,
        TileSpec? moving,
        int cellX,
        int cellY,
        int spanX,
        int spanY,
        int cols,
        int rows)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        if (!IsInside(cellX, cellY, spanX, spanY, cols, rows))
        {
            return false;
        }

        return !tiles.Any(t => !ReferenceEquals(t, moving) && Overlaps(t, cellX, cellY, spanX, spanY));
    }

    public static IReadOnlyList<(int X, int Y)> FreeCells(IEnumerable<TileSpec> tiles, int cols, int rows)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        var list = tiles as IReadOnlyCollection<TileSpec> ?? [.. tiles];
        var free = new List<(int, int)>();

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                if (!list.Any(t => Overlaps(t, x, y, 1, 1)))
                {
                    free.Add((x, y));
                }
            }
        }

        return free;
    }

    public static int NextRotationIndex(IEnumerable<TileSpec> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);

        var used = tiles
            .Where(t => t.Kind == TileKind.Rotating && t.RotationIndex is > 0)
            .Select(t => t.RotationIndex!.Value)
            .ToHashSet();

        var index = 1;
        while (used.Contains(index))
        {
            index++;
        }

        return index;
    }

    private static int[] Distribute(int total, int parts)
    {
        var result = new int[parts];
        var basis = total / parts;
        var extra = total % parts;

        for (var i = 0; i < parts; i++)
        {
            result[i] = basis + (i < extra ? 1 : 0);
        }

        return result;
    }

    private static int[] Offsets(int[] sizes)
    {
        var result = new int[sizes.Length];
        var running = 0;

        for (var i = 0; i < sizes.Length; i++)
        {
            result[i] = running;
            running += sizes[i];
        }

        return result;
    }
}
