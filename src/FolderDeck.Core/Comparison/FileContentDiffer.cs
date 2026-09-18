using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace FolderDeck.Core.Comparison;

/// <summary>
/// 두 파일 내용을 줄 단위로 비교한 판정 한 줄. 기능 후보 ①의 연장(2026-09-18) — 타일
/// 비교가 <see cref="CompareStatus.Different"/>로 표시한 항목의 "실제 내용까지" 보는 자리다.
/// </summary>
public enum ContentDiffLineKind
{
    /// <summary>양쪽 다 같다.</summary>
    Unchanged,

    /// <summary>이쪽에서 지워졌다(상대에는 없다).</summary>
    Deleted,

    /// <summary>이쪽에 새로 생겼다(상대에는 없다).</summary>
    Inserted,

    /// <summary>같은 자리인데 내용이 바뀌었다.</summary>
    Modified,

    /// <summary>줄 정렬을 맞추려고 끼운 빈 자리 — 실제 줄이 아니다(<see cref="ContentDiffLine.Text"/>가 null).</summary>
    Imaginary,
}

/// <summary>왼쪽 또는 오른쪽 한쪽의 줄 하나.</summary>
public sealed record ContentDiffLine(int? LineNumber, string? Text, ContentDiffLineKind Kind);

/// <summary>
/// 두 파일 내용 비교 결과 — 왼쪽·오른쪽 줄 목록. <b>두 목록은 항상 길이가 같다</b>(DiffPlex
/// 가 짧은 쪽에 <see cref="ContentDiffLineKind.Imaginary"/> 빈 자리를 끼워 정렬을 맞춘다) —
/// 화면은 같은 인덱스끼리 나란히 그리면 된다.
/// </summary>
public sealed record ContentDiffResult(IReadOnlyList<ContentDiffLine> Left, IReadOnlyList<ContentDiffLine> Right);

/// <summary>
/// 파일 내용 diff — 순수 함수, DiffPlex(Apache License 2.0, NuGet)를 감싼다.
/// </summary>
/// <remarks>
/// DiffPlex 타입을 밖으로 내보내지 않는다 — <see cref="ContentDiffLine"/>·
/// <see cref="ContentDiffLineKind"/>로만 결과를 낸다, 이 저장소의 다른 경계(예:
/// <c>EverythingNative</c>)가 서드파티 구체 타입을 감싸는 것과 같은 이유다. 줄 단위 비교만
/// 한다 — 바뀐 줄 안에서 단어 단위까지 더 쪼개는 것(DiffPlex의 <c>SubPieces</c>)은 V1
/// 화면이 쓰지 않는다.
/// </remarks>
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
        pieces.Select(p => new ContentDiffLine(p.Position, p.Text, ToKind(p.Type))).ToList();

    private static ContentDiffLineKind ToKind(ChangeType type) => type switch
    {
        ChangeType.Unchanged => ContentDiffLineKind.Unchanged,
        ChangeType.Deleted => ContentDiffLineKind.Deleted,
        ChangeType.Inserted => ContentDiffLineKind.Inserted,
        ChangeType.Modified => ContentDiffLineKind.Modified,
        _ => ContentDiffLineKind.Imaginary,
    };
}
