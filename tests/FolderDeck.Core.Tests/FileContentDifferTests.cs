using FolderDeck.Core.Comparison;

namespace FolderDeck.Core.Tests;

/// <summary>
/// 파일 내용 diff(기능 후보 ①의 연장, 2026-09-18) — DiffPlex를 감싼
/// <see cref="FileContentDiffer"/>의 판정만 본다. 순수 함수라 파일 I/O 없이 문자열만으로
/// 시험한다.
/// </summary>
public sealed class FileContentDifferTests
{
    [Fact]
    public void IdenticalTextsProduceOnlyUnchangedLines()
    {
        var result = FileContentDiffer.Diff("a\nb\nc", "a\nb\nc");

        Assert.All(result.Left, l => Assert.Equal(ContentDiffLineKind.Unchanged, l.Kind));
        Assert.All(result.Right, l => Assert.Equal(ContentDiffLineKind.Unchanged, l.Kind));
        Assert.Equal(["a", "b", "c"], result.Left.Select(l => l.Text));
    }

    [Fact]
    public void LeftAndRightAlwaysHaveTheSameLength()
    {
        var result = FileContentDiffer.Diff("a\nb", "a\nx\ny\nb");

        Assert.Equal(result.Left.Count, result.Right.Count);
    }

    [Fact]
    public void AddedLineShowsAsInsertedOnRightAndImaginaryOnLeft()
    {
        var result = FileContentDiffer.Diff("a\nb", "a\nx\nb");

        var index = result.Right.ToList().FindIndex(l => l.Kind == ContentDiffLineKind.Inserted);
        Assert.True(index >= 0);
        Assert.Equal("x", result.Right[index].Text);
        Assert.Equal(ContentDiffLineKind.Imaginary, result.Left[index].Kind);
        Assert.Null(result.Left[index].Text);
    }

    [Fact]
    public void RemovedLineShowsAsDeletedOnLeftAndImaginaryOnRight()
    {
        var result = FileContentDiffer.Diff("a\nx\nb", "a\nb");

        var index = result.Left.ToList().FindIndex(l => l.Kind == ContentDiffLineKind.Deleted);
        Assert.True(index >= 0);
        Assert.Equal("x", result.Left[index].Text);
        Assert.Equal(ContentDiffLineKind.Imaginary, result.Right[index].Kind);
        Assert.Null(result.Right[index].Text);
    }

    [Fact]
    public void ChangedLineShowsAsModifiedOnBothSidesWithItsOwnText()
    {
        var result = FileContentDiffer.Diff("a\nb\nc", "a\nz\nc");

        Assert.Equal(ContentDiffLineKind.Modified, result.Left[1].Kind);
        Assert.Equal(ContentDiffLineKind.Modified, result.Right[1].Kind);
        Assert.Equal("b", result.Left[1].Text);
        Assert.Equal("z", result.Right[1].Text);
    }

    [Fact]
    public void LineNumbersAreOneBasedAndOmittedForImaginaryLines()
    {
        var result = FileContentDiffer.Diff("a", "a\nb");

        Assert.Equal(1, result.Left[0].LineNumber);
        var insertedIndex = result.Right.ToList().FindIndex(l => l.Kind == ContentDiffLineKind.Inserted);
        Assert.Equal(2, result.Right[insertedIndex].LineNumber);
        Assert.Null(result.Left[insertedIndex].LineNumber);
    }

    [Fact]
    public void EmptyTextsProduceNoLines()
    {
        var result = FileContentDiffer.Diff(string.Empty, string.Empty);

        Assert.Empty(result.Left);
        Assert.Empty(result.Right);
    }
}
