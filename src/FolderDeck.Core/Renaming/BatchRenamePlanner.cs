using IoPath = System.IO.Path;
using System.Globalization;
using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Renaming;

/// <summary>배치 리네임이 지원하는 규칙 종류(V1 — 기능 후보 ③, 정규식은 비목표).</summary>
public enum RenameRuleKind
{
    FindReplace,
    Numbering,
    ChangeCase,
}

public enum CaseChangeKind
{
    Upper,
    Lower,
    TitleCase,
}

/// <summary>
/// 규칙 하나의 매개변수. <see cref="Kind"/> 가 어느 필드를 쓰는지 정한다 — 셋을 한 레코드로
/// 묶은 것은 UI가 라디오로 하나를 고르고 그 옆 입력칸만 채우는 모양과 맞춘 것이다.
/// </summary>
public sealed record RenameRule(
    RenameRuleKind Kind,
    string Find = "",
    string Replace = "",
    string NumberPrefix = "",
    int NumberStart = 1,
    int NumberPadding = 3,
    CaseChangeKind CaseChange = CaseChangeKind.Upper);

/// <summary>미리보기 표 한 줄.</summary>
/// <param name="OriginalName">지금 이름(확장자 포함).</param>
/// <param name="NewName">규칙 적용 뒤 이름.</param>
/// <param name="HasNameConflict">
/// 이 배치 안의 다른 새 이름과 겹치거나, 배치 밖(같은 폴더의 다른 파일)의 이름과 겹친다.
/// </param>
/// <param name="InvalidReason">
/// <see cref="FolderPathRules.RejectLeafName"/> 이 거절한 이유(금칙 문자·예약 이름 등).
/// <c>null</c> 이면 이름 자체는 유효하다.
/// </param>
public sealed record RenamePreviewRow(
    string OriginalName,
    string NewName,
    bool HasNameConflict,
    string? InvalidReason)
{
    /// <summary>실행을 막아야 하는가 — 충돌이 있거나 이름 자체가 무효할 때.</summary>
    public bool IsBlocked => HasNameConflict || InvalidReason is not null;

    /// <summary>이름이 실제로 바뀌는가(제자리 이름은 실행에서 건너뛴다).</summary>
    public bool IsUnchanged => string.Equals(OriginalName, NewName, StringComparison.Ordinal);
}

/// <summary>
/// 배치 리네임(기능 후보 ③, 2026-09-18 설계, <c>doc/FeatureCandidates_2026-09-18_design.md</c>
/// §③)의 순수 이름 변환·미리보기 계산. 디스크는 건드리지 않는다 — 실제 <c>Move</c> 는
/// 부르는 쪽(App 층)의 몫이다.
/// </summary>
public static class BatchRenamePlanner
{
    /// <param name="originalNames">지금 이름 목록(순서가 <see cref="RenameRuleKind.Numbering"/> 의 번호 순서다).</param>
    /// <param name="rule">적용할 규칙.</param>
    /// <param name="otherExistingNames">
    /// 같은 폴더의 <b>배치에 안 낀</b> 다른 항목 이름들 — 이것과 겹치면 배치 밖 충돌이다.
    /// <c>null</c> 이면 배치 안 겹침만 본다.
    /// </param>
    public static IReadOnlyList<RenamePreviewRow> Preview(
        IReadOnlyList<string> originalNames,
        RenameRule rule,
        IReadOnlyCollection<string>? otherExistingNames = null)
    {
        ArgumentNullException.ThrowIfNull(originalNames);
        ArgumentNullException.ThrowIfNull(rule);

        var newNames = Apply(originalNames, rule);
        var otherNames = otherExistingNames is null
            ? []
            : new HashSet<string>(otherExistingNames, StringComparer.OrdinalIgnoreCase);

        var newNameCounts = newNames
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<RenamePreviewRow>(originalNames.Count);
        for (var index = 0; index < originalNames.Count; index++)
        {
            var original = originalNames[index];
            var updated = newNames[index];
            var unchanged = string.Equals(original, updated, StringComparison.Ordinal);

            // 배치 안 중복은 "제자리 이름"도 포함해서 센다 — 다른 항목이 바로 그 이름으로
            // 들어오면, 안 바뀌는 이 항목도 그 자리를 이미 차지하고 있어 충돌이다. 배치
            // 밖(otherExistingNames)과의 충돌만 제자리 이름을 뺀다 — 자기 자신의 지금 이름과
            // 겹치는 것은 충돌이 아니다.
            var conflict = newNameCounts.GetValueOrDefault(updated) > 1
                || (!unchanged && otherNames.Contains(updated));

            // 제자리 이름은 판정에서 뺀다 — 실행 때 건너뛸 이름이라 거절 이유를 보일 필요가 없다.
            var invalidReason = unchanged ? null : FolderPathRules.RejectLeafName(updated);

            rows.Add(new RenamePreviewRow(original, updated, conflict, invalidReason));
        }

        return rows;
    }

    private static IReadOnlyList<string> Apply(IReadOnlyList<string> names, RenameRule rule) => rule.Kind switch
    {
        RenameRuleKind.FindReplace => names.Select(name => ApplyFindReplace(name, rule)).ToList(),
        RenameRuleKind.Numbering => names.Select((name, index) => ApplyNumbering(name, rule, index)).ToList(),
        RenameRuleKind.ChangeCase => names.Select(name => ApplyCaseChange(name, rule.CaseChange)).ToList(),
        _ => names,
    };

    private static string ApplyFindReplace(string name, RenameRule rule) =>
        rule.Find.Length == 0 ? name : name.Replace(rule.Find, rule.Replace, StringComparison.Ordinal);

    private static string ApplyNumbering(string name, RenameRule rule, int index)
    {
        var extension = IoPath.GetExtension(name);
        var number = (rule.NumberStart + index).ToString(CultureInfo.InvariantCulture)
            .PadLeft(Math.Max(0, rule.NumberPadding), '0');
        return $"{rule.NumberPrefix}{number}{extension}";
    }

    private static string ApplyCaseChange(string name, CaseChangeKind kind)
    {
        var extension = IoPath.GetExtension(name);
        var stem = name[..^extension.Length];
        var newStem = kind switch
        {
            CaseChangeKind.Upper => stem.ToUpperInvariant(),
            CaseChangeKind.Lower => stem.ToLowerInvariant(),
            CaseChangeKind.TitleCase =>
                CultureInfo.InvariantCulture.TextInfo.ToTitleCase(stem.ToLowerInvariant()),
            _ => stem,
        };

        return newStem + extension;
    }
}
