using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace FolderDeck.Core.Comparison;

public enum ContentDiffLineKind
{

    Unchanged,

    Deleted,

    Inserted,

    Modified,

    Imaginary,
}

public sealed record ContentDiffSpan(string Text, bool IsChanged);

public sealed record ContentDiffLine(int? LineNumber, string? Text, ContentDiffLineKind Kind, IReadOnlyList<ContentDiffSpan>? Spans = null);

public sealed record ContentDiffResult(IReadOnlyList<ContentDiffLine> Left, IReadOnlyList<ContentDiffLine> Right);

public static class FileContentDiffer
{
    public static ContentDiffResult Diff(string leftText, string rightText)
    {
        ArgumentNullException.ThrowIfNull(leftText);
        ArgumentNullException.ThrowIfNull(rightText);

        var model = SideBySideDiffBuilder.Diff(leftText, rightText);

        return new ContentDiffResult(Convert(model.OldText.Lines), Convert(model.NewText.Lines));
    }

    private static IReadOnlyList<ContentDiffLine> Convert(List<DiffPiece> pieces) =>
        pieces.Select(p => new ContentDiffLine(p.Position, p.Text, ToKind(p.Type), ConvertSpans(p))).ToList();

    private static IReadOnlyList<ContentDiffSpan>? ConvertSpans(DiffPiece piece)
    {
        if (piece.Type != ChangeType.Modified || piece.SubPieces.Count == 0)
        {
            return null;
        }

        return piece.SubPieces
            .Where(sub => sub.Type != ChangeType.Imaginary)
            .Select(sub => new ContentDiffSpan(sub.Text ?? string.Empty, sub.Type != ChangeType.Unchanged))
            .ToList();
    }

    private static ContentDiffLineKind ToKind(ChangeType type) => type switch
    {
        ChangeType.Unchanged => ContentDiffLineKind.Unchanged,
        ChangeType.Deleted => ContentDiffLineKind.Deleted,
        ChangeType.Inserted => ContentDiffLineKind.Inserted,
        ChangeType.Modified => ContentDiffLineKind.Modified,
        _ => ContentDiffLineKind.Imaginary,
    };
}
