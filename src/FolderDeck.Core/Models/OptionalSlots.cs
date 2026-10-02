namespace FolderDeck.Core.Models;

public sealed class GridSpec
{
    public int Cols { get; set; } = 4;
    public int Rows { get; set; } = 4;
}

public enum TileKind
{
    Pinned,
    Rotating,
    Search,
}

public sealed class TileSpec
{

    public Guid? FolderId { get; set; }

    public int CellX { get; set; }

    public int CellY { get; set; }

    public int SpanX { get; set; } = 1;

    public int SpanY { get; set; } = 1;

    public TileKind Kind { get; set; }

    public int? RotationIndex { get; set; }

    public TileSpec Clone() => new()
    {
        FolderId = FolderId,
        CellX = CellX,
        CellY = CellY,
        SpanX = SpanX,
        SpanY = SpanY,
        Kind = Kind,
        RotationIndex = RotationIndex,
    };
}

public sealed class WindowSpec
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public bool Maximized { get; set; }

    public string? MonitorId { get; set; }
}

public enum MacroSourceKind
{
    FixedPath,
    Selection,
}

public enum MacroDestKind
{
    AllVisible,
    Tray,
    FolderIds,
}

public sealed class MacroSource
{
    public MacroSourceKind Kind { get; set; }
    public string? Path { get; set; }
}

public sealed class MacroDest
{
    public MacroDestKind Kind { get; set; }
    public List<Guid>? FolderIds { get; set; }
}

public sealed class MacroDefinition
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public FileOperationKind Op { get; set; }
    public MacroSource? Source { get; set; }
    public MacroDest? Dest { get; set; }
    public ConflictPolicy OnConflict { get; set; }

    public bool Confirm { get; set; } = true;
}
