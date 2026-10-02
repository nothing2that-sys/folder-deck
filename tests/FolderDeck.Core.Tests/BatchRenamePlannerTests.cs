using FolderDeck.Core.Renaming;

namespace FolderDeck.Core.Tests;

public sealed class BatchRenamePlannerTests
{
    [Fact]
    public void FindReplaceSubstitutesEveryOccurrence()
    {
        var rule = new RenameRule(RenameRuleKind.FindReplace, Find: "draft", Replace: "final");
        var rows = BatchRenamePlanner.Preview(["draft-draft.txt", "other.txt"], rule);

        Assert.Equal("final-final.txt", rows[0].NewName);
        Assert.Equal("other.txt", rows[1].NewName);
        Assert.True(rows[1].IsUnchanged);
    }

    [Fact]
    public void NumberingPadsAndKeepsExtensionAndOrder()
    {
        var rule = new RenameRule(
            RenameRuleKind.Numbering, NumberPrefix: "img-", NumberStart: 1, NumberPadding: 3);
        var rows = BatchRenamePlanner.Preview(["b.jpg", "a.jpg", "c.png"], rule);

        Assert.Equal(["img-001.jpg", "img-002.jpg", "img-003.png"], rows.Select(r => r.NewName));
    }

    [Theory]
    [InlineData(CaseChangeKind.Upper, "REPORT.txt")]
    [InlineData(CaseChangeKind.Lower, "report.txt")]
    [InlineData(CaseChangeKind.TitleCase, "Report.txt")]
    public void CaseChangeLeavesExtensionAlone(CaseChangeKind kind, string expected)
    {
        var rule = new RenameRule(RenameRuleKind.ChangeCase, CaseChange: kind);
        var rows = BatchRenamePlanner.Preview(["rEPort.txt"], rule);

        Assert.Equal(expected, Assert.Single(rows).NewName);
    }

    [Fact]
    public void TwoItemsCollapsingToTheSameNameConflictWithEachOther()
    {
        var rule = new RenameRule(RenameRuleKind.FindReplace, Find: "-v1", Replace: "");
        var rows = BatchRenamePlanner.Preview(["doc-v1.txt", "doc.txt"], rule);

        Assert.True(rows[0].HasNameConflict);
        Assert.True(rows[1].HasNameConflict);
    }

    [Fact]
    public void CollidingWithAnUnselectedExistingFileIsAConflict()
    {
        var rule = new RenameRule(RenameRuleKind.FindReplace, Find: "old", Replace: "new");
        var rows = BatchRenamePlanner.Preview(["old.txt"], rule, otherExistingNames: ["new.txt"]);

        Assert.True(Assert.Single(rows).HasNameConflict);
    }

    [Fact]
    public void UnchangedNameIsNeverFlaggedAsConflictEvenIfItMatchesItself()
    {
        var rule = new RenameRule(RenameRuleKind.FindReplace, Find: "nope", Replace: "x");
        var rows = BatchRenamePlanner.Preview(["keep.txt"], rule, otherExistingNames: ["keep.txt"]);

        var row = Assert.Single(rows);
        Assert.True(row.IsUnchanged);
        Assert.False(row.HasNameConflict);
    }

    [Fact]
    public void InvalidCharactersFromARuleAreCaughtByTheSharedPathRules()
    {

        var rule = new RenameRule(RenameRuleKind.FindReplace, Find: ".", Replace: ":");
        var rows = BatchRenamePlanner.Preview(["a.txt"], rule);

        Assert.NotNull(Assert.Single(rows).InvalidReason);
    }

    [Fact]
    public void UnchangedNameSkipsValidation()
    {

        var rule = new RenameRule(RenameRuleKind.FindReplace, Find: "nope", Replace: "x");
        var rows = BatchRenamePlanner.Preview(["a.txt"], rule);

        Assert.Null(Assert.Single(rows).InvalidReason);
    }

    [Fact]
    public void IsBlockedIsTrueForEitherConflictOrInvalidReason()
    {
        var conflictOnly = new RenamePreviewRow("a", "b", HasNameConflict: true, InvalidReason: null);
        var invalidOnly = new RenamePreviewRow("a", "b", HasNameConflict: false, InvalidReason: "bad");
        var clean = new RenamePreviewRow("a", "b", HasNameConflict: false, InvalidReason: null);

        Assert.True(conflictOnly.IsBlocked);
        Assert.True(invalidOnly.IsBlocked);
        Assert.False(clean.IsBlocked);
    }
}
