using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Comparison;

/// <summary>
/// 타일 간 비교 한 항목의 판정. 기능 후보 ①(2026-09-18 설계,
/// <c>doc/FeatureCandidates_2026-09-18_design.md</c> §①).
/// </summary>
public enum CompareStatus
{
    /// <summary>비교가 걸려 있지 않다(기본값).</summary>
    Unset,

    /// <summary>이 타일에만 있다.</summary>
    OnlyHere,

    /// <summary>양쪽에 있지만 다르다(폴더/파일 종류가 다르거나, 파일이면 크기·수정일이 다르다).</summary>
    Different,

    /// <summary>양쪽에 같은 것으로 본다.</summary>
    Same,
}

/// <summary>비교 결과 한 줄 — 원래 항목 + 그 판정.</summary>
public sealed record CompareEntry(FolderItem Item, CompareStatus Status);

/// <summary>
/// 두 타일(폴더)의 목록을 비교하는 순수 함수. 이름으로 짝짓고(대소문자 무시 — Windows
/// 파일시스템 기본), 파일은 크기·수정일로, 폴더는 존재 여부로만 같다/다르다를 가른다.
/// </summary>
/// <remarks>
/// <b>재귀 비교는 하지 않는다(V1 비목표)</b> — 같은 이름의 폴더끼리는 내용을 안 열어 보고
/// 항상 <see cref="CompareStatus.Same"/> 으로 본다. <b>내용(바이트) 비교도 하지 않는다</b> —
/// <see cref="FolderItem"/> 에 해시가 없고 이 저장소에 해시 코드가 없다(2026-09-18 조사,
/// <c>doc/Probe_2026-09-18_followup_report.md</c> §4). 이름+크기+수정일만으로는 서로 다른
/// 내용이 우연히 같은 값을 가지면(드묾) "같다"로 오판할 수 있다 — 이건 설계가 받아들인
/// 한계다.
/// </remarks>
public static class TileComparer
{
    public static (IReadOnlyList<CompareEntry> Left, IReadOnlyList<CompareEntry> Right) Compare(
        IReadOnlyList<FolderItem> left, IReadOnlyList<FolderItem> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftByName = left.ToLookup(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var rightByName = right.ToLookup(item => item.Name, StringComparer.OrdinalIgnoreCase);

        var leftResult = left
            .Select(item => new CompareEntry(item, StatusOf(item, rightByName[item.Name].FirstOrDefault())))
            .ToList();

        var rightResult = right
            .Select(item => new CompareEntry(item, StatusOf(item, leftByName[item.Name].FirstOrDefault())))
            .ToList();

        return (leftResult, rightResult);
    }

    private static CompareStatus StatusOf(FolderItem item, FolderItem? counterpart)
    {
        if (counterpart is null)
        {
            return CompareStatus.OnlyHere;
        }

        if (item.IsDirectory != counterpart.IsDirectory)
        {
            return CompareStatus.Different;
        }

        if (item.IsDirectory)
        {
            return CompareStatus.Same;
        }

        return item.Size == counterpart.Size && item.ModifiedUtc == counterpart.ModifiedUtc
            ? CompareStatus.Same
            : CompareStatus.Different;
    }
}
