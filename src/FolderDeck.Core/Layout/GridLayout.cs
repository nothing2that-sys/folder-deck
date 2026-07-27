using FolderDeck.Core.Models;

namespace FolderDeck.Core.Layout;

/// <summary>
/// 4x4 그리드 배치의 순수 규칙 (§3.2 · Phase 2). UI 없이 검증 가능하도록 Core 에 둔다.
/// </summary>
/// <remarks>
/// 겹침은 <b>거부</b>한다(밀어내기 안 함). 밀어내기는 한 번의 드래그가 배치 전체를 흔들어
/// "무엇이 어디로 갔는지" 예측할 수 없게 만든다 — 못 놓는 자리는 못 놓는 것으로 보이는 편이 낫다.
/// </remarks>
public static class GridLayout
{
    public const int DefaultCols = 4;
    public const int DefaultRows = 4;

    /// <summary>
    /// 그리드 슬롯을 세운다. 렌더러가 4x4 고정이므로(§3.2) 다른 값은 4x4 로 되돌린다 —
    /// 모델만 3x3 이고 화면은 4x4 인 상태를 만들지 않기 위해서다.
    /// 가변 그리드는 스키마와 렌더러를 같이 바꾸는 별도 건이다.
    /// </summary>
    public static GridSpec EnsureGrid(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var grid = workspace.Grid ??= new GridSpec();
        grid.Cols = DefaultCols;
        grid.Rows = DefaultRows;
        return grid;
    }

    /// <summary>
    /// v1/v2 → v3 마이그레이션. <c>tiles[]</c> 가 없거나 비어 있으면 기존 유도 규칙
    /// (상시 폴더마다 상시 타일 + 순환 타일 하나)으로 한 번 만든다.
    /// </summary>
    /// <returns>실제로 만들었으면 true — 호출부가 저장해서 v3 로 승격시킨다.</returns>
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

    /// <summary>
    /// Phase 1 의 배치를 그대로 옮긴 것 — 상시 폴더마다 타일 하나, 마지막에 순환1 타일 하나.
    /// 좌표는 단순 균등 분할로 채운다(칸이 남으면 비워 둔다).
    /// </summary>
    public static List<TileSpec> DeriveFromPinned(IReadOnlyList<FolderEntry> folders, int cols, int rows)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var capacity = cols * rows;
        var pinned = folders.Where(f => f.Pinned).ToList();

        // 순환 칸 하나는 항상 들어간다 — 왼쪽 리스트 클릭이 갈 곳이 없으면 앱이 반쪽이 된다.
        // 16칸을 넘는 상시 폴더는 타일 없이 folders[] 에만 남는다(리스트에서는 계속 보인다).
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

    /// <summary>
    /// 손으로 고쳤거나 그리드가 줄어든 파일을 열 수 있게 다듬는다. 격자 밖으로 나간 타일은
    /// 안으로 당기고, 그래도 겹치거나 폴더가 사라진 타일은 뺀다.
    /// </summary>
    /// <returns>뺀 타일 수. 0 이 아니면 호출부가 알린다 — 조용히 삼키지 않는다(§5).</returns>
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
            // 상시 칸인데 폴더가 사라졌으면 남길 근거가 없다.
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

        // 같은 번호를 쓰는 순환 칸이 둘이면 뒤엣것에 새 번호를 준다 — 클릭이 어디로 갈지
        // 정해지지 않는 상태를 남기지 않는다.
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

    /// <summary>격자 안에 온전히 들어가는가.</summary>
    public static bool IsInside(int cellX, int cellY, int spanX, int spanY, int cols, int rows) =>
        spanX >= 1 && spanY >= 1
        && cellX >= 0 && cellY >= 0
        && cellX + spanX <= cols
        && cellY + spanY <= rows;

    /// <summary>두 사각형이 한 칸이라도 겹치는가.</summary>
    public static bool Overlaps(TileSpec tile, int cellX, int cellY, int spanX, int spanY)
    {
        ArgumentNullException.ThrowIfNull(tile);

        return cellX < tile.CellX + tile.SpanX
               && tile.CellX < cellX + spanX
               && cellY < tile.CellY + tile.SpanY
               && tile.CellY < cellY + spanY;
    }

    /// <summary>
    /// <paramref name="moving"/> 을 그 자리에 놓을 수 있는가. 자기 자신과의 겹침은 세지 않는다
    /// (새 타일이면 <paramref name="moving"/> 에 null).
    /// </summary>
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

    /// <summary>비어 있는 1x1 칸 좌표들 (좌→우, 위→아래).</summary>
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

    /// <summary>이미 쓰이지 않는 가장 낮은 순환 번호 (순환1 이 있으면 순환2).</summary>
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

    /// <summary>총량을 조각 수로 나눈다 — 나머지는 앞쪽부터. (4, 3) → [2, 1, 1].</summary>
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
