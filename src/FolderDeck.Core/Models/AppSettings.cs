namespace FolderDeck.Core.Models;

public sealed class AppSettings
{

    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; }

    public bool SkipLauncher { get; set; }

    public Guid? LastWorkspaceId { get; set; }

    public List<Guid>? WorkspaceOrder { get; set; }

    public static readonly IReadOnlyList<int> TileGapChoices = [4, 6, 8, 12, 16];

    public const int DefaultTileGap = 8;

    private int _tileGap = DefaultTileGap;

    public int TileGap
    {
        get => _tileGap;
        set => _tileGap = TileGapChoices.Contains(value) ? value : DefaultTileGap;
    }

    private Dictionary<string, string> _extensionGlyphs = [];

    public Dictionary<string, string> ExtensionGlyphs
    {
        get => _extensionGlyphs;
        set => _extensionGlyphs = value ?? [];
    }

    public static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var lower = trimmed.ToLowerInvariant();
        return lower[0] == '.' ? lower : "." + lower;
    }

    private bool _showShellIcons = true;

    public bool ShowShellIcons
    {
        get => _showShellIcons;
        set => _showShellIcons = value;
    }

    public static readonly IReadOnlyList<int> EverythingMaxResultsChoices = [200, 500, 1000, 2000, 5000];

    public const int DefaultEverythingMaxResults = 1000;

    private int _everythingMaxResults = DefaultEverythingMaxResults;

    public int EverythingMaxResults
    {
        get => _everythingMaxResults;
        set => _everythingMaxResults = EverythingMaxResultsChoices.Contains(value) ? value : DefaultEverythingMaxResults;
    }
}
