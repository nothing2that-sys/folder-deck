namespace FolderDeck.Core.Models;

public sealed class Workspace
{

    public const int CurrentSchemaVersion = 11;

    public int SchemaVersion { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset LastUsed { get; set; }

    public List<FolderEntry> Folders { get; set; } = [];

    public LeftLayoutSpec? LeftLayout { get; set; }

    public GridSpec? Grid { get; set; }

    public List<TileSpec>? Tiles { get; set; }

    public ConflictPolicy? OnConflict { get; set; }

    public bool? AskOnConflict { get; set; }

    public WindowSpec? Window { get; set; }

    public List<Guid>? CopyTray { get; set; }

    public List<MacroDefinition>? Macros { get; set; }
}
