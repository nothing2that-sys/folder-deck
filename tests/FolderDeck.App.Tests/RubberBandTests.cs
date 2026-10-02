using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

public sealed class RubberBandTests
{

    [Fact]
    public void PressingSomethingThatIsNeitherAnItemNorAScrollBarIsEmptySpace() =>
        Assert.Equal(
            ListHitKind.Empty,
            RubberBand.Classify(
                [typeof(Border), typeof(ScrollContentPresenter), typeof(ScrollViewer), typeof(ListView)]));

    [Fact]
    public void AnEmptyAncestorChainIsEmptySpace() =>
        Assert.Equal(ListHitKind.Empty, RubberBand.Classify([]));

    [Fact]
    public void AnythingInsideARowIsAnItemEvenTheEmptySpaceToTheRightOfTheName()
    {

        Assert.Equal(
            ListHitKind.Item,
            RubberBand.Classify(
                [typeof(Border), typeof(GridViewRowPresenter), typeof(ListViewItem), typeof(ListView)]));

        Assert.Equal(
            ListHitKind.Item,
            RubberBand.Classify([typeof(TextBlock), typeof(ListViewItem), typeof(ListView)]));
    }

    [Fact]
    public void ASubclassOfARowIsStillARow() =>
        Assert.Equal(ListHitKind.Item, RubberBand.Classify([typeof(DerivedRow)]));

    private sealed class DerivedRow : ListViewItem;

    [Fact]
    public void PressingTheScrollBarIsNeitherRubberBandNorDrag()
    {
        Assert.Equal(ListHitKind.None, RubberBand.Classify([typeof(Thumb), typeof(Track), typeof(ScrollBar)]));
        Assert.Equal(ListHitKind.None, RubberBand.Classify([typeof(Border), typeof(ScrollBar)]));

        Assert.Equal(ListHitKind.None, RubberBand.Classify([typeof(Thumb)]));
    }

    [Fact]
    public void TheScrollBarWinsEvenWhenAnItemIsCloserInTheChain() =>
        Assert.Equal(
            ListHitKind.None,
            RubberBand.Classify([typeof(ListViewItem), typeof(ScrollBar), typeof(ListView)]));

    [Fact]
    public void NoSourceMeansNoChain()
    {
        Assert.Empty(RubberBand.AncestorTypes(null, null));
        Assert.Empty(RubberBand.AncestorTypes("요소가 아니다", null));

        Assert.Equal(ListHitKind.Empty, RubberBand.Classify(RubberBand.AncestorTypes(null, null)));
    }

    private static readonly Rect Row = new(0, 20, 200, 20);

    [Fact]
    public void ARowThatOverlapsTheBandIsPicked()
    {

        Assert.True(RubberBand.Touches(new Rect(0, 0, 10, 21), Row));

        Assert.True(RubberBand.Touches(new Rect(0, 0, 300, 100), Row));

        Assert.True(RubberBand.Touches(new Rect(50, 25, 10, 5), Row));
    }

    [Fact]
    public void ARowThatOnlyTouchesTheBandEdgeIsNotPicked()
    {

        Assert.False(RubberBand.Touches(new Rect(0, 0, 200, 20), Row));
        Assert.True(RubberBand.Touches(new Rect(0, 0, 200, 20.5), Row));

        Assert.False(RubberBand.Touches(new Rect(200, 20, 50, 20), Row));
        Assert.True(RubberBand.Touches(new Rect(199.5, 20, 50, 20), Row));
    }

    [Fact]
    public void ARowThatMissesTheBandEntirelyIsNotPicked()
    {
        Assert.False(RubberBand.Touches(new Rect(0, 0, 200, 10), Row));
        Assert.False(RubberBand.Touches(new Rect(0, 60, 200, 10), Row));
        Assert.False(RubberBand.Touches(new Rect(300, 20, 50, 20), Row));
    }

    [Fact]
    public void AZeroWidthBandStillPicksTheRowsItPassesThrough()
    {
        Assert.True(RubberBand.Touches(new Rect(100, 0, 0, 100), Row));

        Assert.False(RubberBand.Touches(new Rect(250, 0, 0, 100), Row));

        Assert.False(RubberBand.Touches(new Rect(0, 0, 0, 100), Row));
    }

    [Fact]
    public void DraggingBackwardsMakesTheSameRectangle()
    {
        var down = RubberBand.Between(new Point(10, 10), new Point(50, 40));
        var up = RubberBand.Between(new Point(50, 40), new Point(10, 10));

        Assert.Equal(down, up);
        Assert.Equal(new Rect(10, 10, 40, 30), down);
    }

    [Fact]
    public void NotMovingFarEnoughIsAClickNotARubberBand()
    {
        var origin = new Point(100, 100);

        Assert.False(RubberBand.PastThreshold(origin, origin));
        Assert.False(RubberBand.PastThreshold(origin, new Point(100.5, 100.5)));
    }

    [Fact]
    public void CrossingTheThresholdOnEitherAxisStartsTheRubberBand()
    {
        var origin = new Point(100, 100);
        var h = SystemParameters.MinimumHorizontalDragDistance;
        var v = SystemParameters.MinimumVerticalDragDistance;

        Assert.True(RubberBand.PastThreshold(origin, new Point(100 + h, 100)));
        Assert.True(RubberBand.PastThreshold(origin, new Point(100, 100 + v)));

        Assert.True(RubberBand.PastThreshold(origin, new Point(100 - h, 100)));
        Assert.True(RubberBand.PastThreshold(origin, new Point(100, 100 - v)));

        Assert.False(RubberBand.PastThreshold(origin, new Point(100 + h - 0.01, 100 + v - 0.01)));
    }

    [Fact]
    public void StartingWithoutControlThrowsAwayTheOldSelection() =>
        Assert.Empty(RubberBand.Kept(adds: false, ["가", "나"]));

    [Fact]
    public void StartingWithControlKeepsTheOldSelection() =>
        Assert.Equal(["가", "나"], RubberBand.Kept(adds: true, ["가", "나"]));

    [Fact]
    public void TheKeptSelectionIsASnapshotNotALiveView()
    {
        var live = new List<object> { "가", "나" };
        var kept = RubberBand.Kept(adds: true, live);

        live.Clear();

        Assert.Equal(2, kept.Count);
    }

    private static (object Item, Rect Bounds) RowAt(string name, double top) =>
        (name, new Rect(0, top, 200, 20));

    [Fact]
    public void TheBandPicksTheRowsItOverlaps()
    {
        var rows = new[] { RowAt("가", 0), RowAt("나", 20), RowAt("다", 40) };

        var picked = RubberBand.Wanted([], rows, new Rect(0, 10, 100, 20));

        Assert.Equal<object>(["가", "나"], picked.OrderBy(o => (string)o));
    }

    [Fact]
    public void ControlAddsToTheSelectionInsteadOfReplacingIt()
    {
        var rows = new[] { RowAt("가", 0), RowAt("나", 20), RowAt("다", 40) };

        var picked = RubberBand.Wanted(["다"], rows, new Rect(0, 0, 100, 5));

        Assert.Equal<object>(["가", "다"], picked.OrderBy(o => (string)o));
    }

    [Fact]
    public void WithoutControlOnlyWhatTheBandOverlapsSurvives()
    {
        var rows = new[] { RowAt("가", 0), RowAt("나", 20), RowAt("다", 40) };

        var picked = RubberBand.Wanted(
            RubberBand.Kept(adds: false, ["다"]), rows, new Rect(0, 0, 100, 5));

        Assert.Equal<object>(["가"], picked);
    }

    [Fact]
    public void AnItemThatIsBothKeptAndOverlappedIsPickedOnce()
    {
        var picked = RubberBand.Wanted(["가"], [RowAt("가", 0)], new Rect(0, 0, 100, 100));

        Assert.Single(picked);
    }

    [Fact]
    public void AnEmptyListPicksNothingAndDoesNotThrow() =>
        Assert.Empty(RubberBand.Wanted([], [], new Rect(0, 0, 500, 500)));

    [Fact]
    public void OnlyRealizedRowsCanBePicked()
    {

        var onScreen = new[] { RowAt("가", 0), RowAt("나", 20) };

        var picked = RubberBand.Wanted([], onScreen, new Rect(0, 0, 500, 5000));

        Assert.Equal(2, picked.Count);
    }
}
