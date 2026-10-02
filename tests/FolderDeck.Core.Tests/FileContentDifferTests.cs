using FolderDeck.Core.Comparison;

namespace FolderDeck.Core.Tests;

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

    [Fact]
    public void ModifiedLineCarriesWordLevelSpansPinpointingTheChange()
    {
        var result = FileContentDiffer.Diff("the quick fox", "the slow fox");

        var left = result.Left[0];
        var right = result.Right[0];
        Assert.Equal(ContentDiffLineKind.Modified, left.Kind);
        Assert.NotNull(left.Spans);
        Assert.NotNull(right.Spans);

        Assert.Contains(left.Spans!, s => s.IsChanged && s.Text.Contains("quick"));
        Assert.Contains(right.Spans!, s => s.IsChanged && s.Text.Contains("slow"));

        Assert.Contains(left.Spans!, s => !s.IsChanged);
        Assert.Contains(right.Spans!, s => !s.IsChanged);
    }

    [Fact]
    public void ConcatenatingSpansReconstructsTheOriginalLineText()
    {
        var result = FileContentDiffer.Diff("the quick brown fox", "the slow brown dog");

        Assert.Equal("the quick brown fox", string.Concat(result.Left[0].Spans!.Select(s => s.Text)));
        Assert.Equal("the slow brown dog", string.Concat(result.Right[0].Spans!.Select(s => s.Text)));
    }

    [Fact]
    public void NonModifiedLinesHaveNoSpans()
    {
        var result = FileContentDiffer.Diff("a\nb", "a\nb\nc");

        Assert.All(result.Left, l => Assert.Null(l.Spans));
        Assert.All(result.Right, l => Assert.Null(l.Spans));
    }
}
