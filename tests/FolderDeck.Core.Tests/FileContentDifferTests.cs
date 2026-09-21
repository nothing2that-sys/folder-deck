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

    /// <summary>
    /// JH 지적(2026-09-21): 줄 전체를 "다르다"로 뭉뚱그리면 실제로 어디가 다른지 눈으로
    /// 찾아야 한다 — 단어 단위로 어디가 바뀌었는지 짚어 줘야 한다. DiffPlex 의
    /// <c>SubPieces</c>(단어 단위 하위 diff)를 <see cref="ContentDiffSpan"/>으로 낸다.
    /// </summary>
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

        // 안 바뀐 부분("the "· " fox")은 안 바뀐 토막으로 남아야 한다 — 줄 전체를
        // "바뀜"으로 뭉개면 안 된다.
        Assert.Contains(left.Spans!, s => !s.IsChanged);
        Assert.Contains(right.Spans!, s => !s.IsChanged);
    }

    /// <summary>토막을 순서대로 이으면 원래 줄 그대로 나와야 한다 — 화면이 이걸 나란히
    /// 이어 그리는 게 전부라, 이 불변이 깨지면 글자가 빠지거나 겹쳐 보인다.</summary>
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
