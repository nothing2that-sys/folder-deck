using IoPath = System.IO.Path;
using System.Globalization;
using FolderDeck.Core.Paths;

namespace FolderDeck.Core.Renaming;

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

public sealed record RenameRule(
    RenameRuleKind Kind,
    string Find = "",
    string Replace = "",
    string NumberPrefix = "",
    int NumberStart = 1,
    int NumberPadding = 3,
    CaseChangeKind CaseChange = CaseChangeKind.Upper);

public sealed record RenamePreviewRow(
    string OriginalName,
    string NewName,
    bool HasNameConflict,
    string? InvalidReason)
{

    public bool IsBlocked => HasNameConflict || InvalidReason is not null;

    public bool IsUnchanged => string.Equals(OriginalName, NewName, StringComparison.Ordinal);
}

public static class BatchRenamePlanner
{

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

            var conflict = newNameCounts.GetValueOrDefault(updated) > 1
                || (!unchanged && otherNames.Contains(updated));

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
