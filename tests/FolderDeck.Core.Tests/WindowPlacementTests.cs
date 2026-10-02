using FolderDeck.Core.Layout;
using FolderDeck.Core.Models;

namespace FolderDeck.Core.Tests;

public sealed class WindowPlacementTests
{

    private static readonly ScreenRect Primary = new(0, 0, 2560, 1392);

    private static readonly ScreenRect Right = new(2560, 108, 1080, 1872);

    private static readonly ScreenRect Left = new(-1440, -271, 1440, 2512);

    private static readonly ScreenRect[] Screens = [Primary, Right, Left];

    private static WindowSpec Spec(double x, double y, double w, double h, bool maximized = false) =>
        new() { X = x, Y = y, Width = w, Height = h, Maximized = maximized };

    private static WindowPlacementResult Resolve(WindowSpec? saved) =>
        WindowPlacement.Resolve(saved, Screens, primaryIndex: 0);

    [Fact]
    public void ASavedPositionInsideTheWorkAreaComesBackUnchanged()
    {
        var result = Resolve(Spec(300, 200, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(new ScreenRect(300, 200, 1400, 900), result.Bounds);
        Assert.False(result.Maximized);
    }

    [Fact]
    public void NegativeCoordinatesAreNotOutsideTheScreen()
    {
        var result = Resolve(Spec(-1340, -171, 1200, 2000));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(-1340, result.Bounds.X);
        Assert.Equal(-171, result.Bounds.Y);
    }

    [Fact]
    public void AWindowStraddlingTwoMonitorsIsKept()
    {
        var result = Resolve(Spec(2200, 300, 800, 600));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(2200, result.Bounds.X);
    }

    [Fact]
    public void APositionThatTouchesNoMonitorFallsBackToTheCenterOfThePrimary()
    {
        var result = Resolve(Spec(-30000, -30000, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.Equal(new ScreenRect(580, 246, 1400, 900), result.Bounds);
    }

    [Fact]
    public void NoSavedPositionMeansTheDefaultPlaceAndSize()
    {
        var result = Resolve(null);

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.Equal(WindowPlacement.DefaultWidth, result.Bounds.Width);
        Assert.Equal(WindowPlacement.DefaultHeight, result.Bounds.Height);
    }

    [Theory]
    [InlineData(0, 900)]
    [InlineData(1400, 0)]
    [InlineData(-1400, 900)]
    public void ASavedSizeThatIsNotUsableFallsBack(double width, double height)
    {
        var result = Resolve(Spec(300, 200, width, height));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void AWindowOverlappingByOnePixelIsNotRecoverable()
    {

        var result = Resolve(Spec(Right.Right - 1, 300, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void AWindowWhoseTitleBarIsAboveTheWorkAreaIsNotRecoverable()
    {

        var result = Resolve(Spec(300, -800, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void TheSameTopEdgeIsFineWhenAMonitorActuallyStartsThere()
    {
        var result = Resolve(Spec(-1000, -271, 900, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
    }

    [Fact]
    public void OverlapExactlyAtTheMinimumWidthIsAccepted()
    {
        var x = Left.X + WindowPlacement.MinVisibleWidth - 1400;

        var result = Resolve(Spec(x, 300, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
        Assert.Equal(x, result.Bounds.X);
    }

    [Fact]
    public void OverlapOneShortOfTheMinimumWidthIsRejected()
    {
        var x = Left.X + WindowPlacement.MinVisibleWidth - 1400 - 1;

        var result = Resolve(Spec(x, 300, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void OverlapExactlyAtTheMinimumHeightIsAccepted()
    {
        var y = Primary.Bottom - WindowPlacement.MinVisibleHeight;

        var result = Resolve(Spec(300, y, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Saved, result.Decision);
    }

    [Fact]
    public void OverlapOneShortOfTheMinimumHeightIsRejected()
    {
        var y = Primary.Bottom - WindowPlacement.MinVisibleHeight + 1;

        var result = Resolve(Spec(300, y, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void AWindowHiddenBehindTheTaskbarIsNotRecoverable()
    {
        var result = Resolve(Spec(300, Primary.Bottom + 10, 1400, 900));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
    }

    [Fact]
    public void AWindowWiderThanTheWorkAreaIsShrunkToFit()
    {
        var result = Resolve(Spec(2560, 108, 3000, 1000));

        Assert.Equal(WindowPlacementDecision.Resized, result.Decision);
        Assert.Equal(Right.Width, result.Bounds.Width);
        Assert.Equal(1000, result.Bounds.Height);
        Assert.Equal(2560, result.Bounds.X);
        Assert.Equal(108, result.Bounds.Y);
    }

    [Fact]
    public void AWindowTallerThanTheWorkAreaIsShrunkToFit()
    {
        var result = Resolve(Spec(100, 0, 1400, 4000));

        Assert.Equal(WindowPlacementDecision.Resized, result.Decision);
        Assert.Equal(Primary.Height, result.Bounds.Height);
        Assert.Equal(1400, result.Bounds.Width);
    }

    [Fact]
    public void ShrinkingUsesTheMonitorTheWindowMostlySitsOn()
    {

        var result = Resolve(Spec(-1400, 0, 2000, 800));

        Assert.Equal(WindowPlacementDecision.Resized, result.Decision);
        Assert.Equal(Left.Width, result.Bounds.Width);
        Assert.Equal(-1400, result.Bounds.X);
    }

    [Fact]
    public void TheDefaultSizeIsShrunkWhenTheWorkAreaIsSmaller()
    {
        ScreenRect[] small = [new(0, 0, 1024, 768)];

        var result = WindowPlacement.Resolve(Spec(-30000, -30000, 1400, 900), small, 0);

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.Equal(new ScreenRect(0, 0, 1024, 768), result.Bounds);
    }

    [Fact]
    public void NoMonitorsAtAllStillProducesAUsableRectangle()
    {
        var result = WindowPlacement.Resolve(Spec(300, 200, 1400, 900), [], 0);

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.True(result.Bounds.IsUsable);
    }

    [Fact]
    public void AnOutOfRangePrimaryIndexFallsBackToTheFirstMonitor()
    {
        var result = WindowPlacement.Resolve(null, Screens, primaryIndex: 7);

        Assert.Equal(Primary.X + ((Primary.Width - WindowPlacement.DefaultWidth) / 2), result.Bounds.X);
    }

    [Fact]
    public void TheMaximizedFlagSurvivesRestoreUntouched()
    {
        var result = Resolve(Spec(300, 200, 1400, 900, maximized: true));

        Assert.True(result.Maximized);
        Assert.Equal(new ScreenRect(300, 200, 1400, 900), result.Bounds);
    }

    [Fact]
    public void TheMaximizedFlagSurvivesTheFallbackToo()
    {
        var result = Resolve(Spec(-30000, -30000, 1400, 900, maximized: true));

        Assert.Equal(WindowPlacementDecision.Fallback, result.Decision);
        Assert.True(result.Maximized);
    }

    [Fact]
    public void IntersectOfDisjointRectanglesHasNoArea()
    {
        var overlap = new ScreenRect(0, 0, 100, 100).Intersect(new ScreenRect(500, 500, 100, 100));

        Assert.Equal(0, overlap.Area);
        Assert.False(overlap.IsUsable);
    }

    [Fact]
    public void CanBeGrabbedIsFalseWhenTheWindowIsNotUsable()
    {
        Assert.False(WindowPlacement.CanBeGrabbed(new ScreenRect(0, 0, 0, 0), Primary));
    }
}
