using FolderDeck.Core.Enumeration;

namespace FolderDeck.Core.Comparison;

/// <summary>중복 후보 그룹 안의 항목 하나 — 어느 타일(이름표)에서 왔는지 같이 든다.</summary>
public sealed record DuplicateGroupEntry(string TileLabel, FolderItem Item);

/// <summary>이름·크기가 같은 후보 중복 그룹.</summary>
public sealed record DuplicateGroup(string Name, long Size, IReadOnlyList<DuplicateGroupEntry> Entries);

/// <summary>
/// 워크스페이스 내 중복 파일 찾기(기능 후보 ②, 2026-09-18 설계,
/// <c>doc/FeatureCandidates_2026-09-18_design.md</c> §②)의 순수 그룹핑 로직.
/// </summary>
/// <remarks>
/// <b>이름+크기가 같은 파일만 후보로 본다</b> — 내용(바이트) 비교는 안 한다(해시 인프라 없음,
/// <see cref="TileComparer"/> 와 같은 이유). <b>폴더는 대상이 아니다</b> — 폴더는 크기가
/// 없다(<see cref="FolderItem.Size"/> 가 재귀 계산되지 않아 늘 <c>null</c>).
/// </remarks>
public static class DuplicateFinder
{
    public static IReadOnlyList<DuplicateGroup> FindGroups(
        IEnumerable<(string TileLabel, FolderItem Item)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(entry => !entry.Item.IsDirectory && entry.Item.Size is not null)
            .GroupBy(
                entry => (Key: entry.Item.Name.ToUpperInvariant(), Size: entry.Item.Size!.Value))
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateGroup(
                group.First().Item.Name,
                group.Key.Size,
                group.Select(entry => new DuplicateGroupEntry(entry.TileLabel, entry.Item)).ToList()))
            .OrderByDescending(group => group.Size)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
