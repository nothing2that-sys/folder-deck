namespace FolderDeck.Core.Models;

public sealed class FolderEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Path { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public FolderViewMode ViewMode { get; set; } = FolderViewMode.List;

    public SortBy SortBy { get; set; } = SortBy.Name;

    public bool SortDesc { get; set; }

    public bool FoldersFirst { get; set; } = true;

    public bool ShowSize { get; set; } = true;

    public bool ShowModified { get; set; } = true;

    public bool ShowPreview { get; set; } = false;

    public const double DefaultPreviewRatio = 0.35;

    private double _previewRatio = DefaultPreviewRatio;

    public double PreviewRatio
    {
        get => _previewRatio;
        set => _previewRatio = value is >= 0.0 and <= 1.0 ? value : DefaultPreviewRatio;
    }

    public bool Pinned { get; set; }

    public int? RotationSlot { get; set; }
}
