using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

public sealed class ColumnLayoutTests
{

    [Fact]
    public void RoomySpaceGoesToTheFlexColumn()
    {

        var width = ColumnLayout.FlexWidth(427, 220, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth);

        Assert.Equal(169, width);

        Assert.Equal(427 - ColumnLayout.RightMargin, width + 220 + ColumnLayout.RowChrome);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(427)]
    [InlineData(683)]
    [InlineData(1600)]
    public void TheRightMarginIsAlwaysKept(double viewport)
    {
        var width = ColumnLayout.FlexWidth(viewport, 220, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth);
        var rowWidth = width + 220 + ColumnLayout.RowChrome;

        if (width > ColumnLayout.MinFlexWidth)
        {
            Assert.True(
                viewport - rowWidth >= ColumnLayout.RightMargin,
                $"뷰포트 {viewport} 에서 여백이 {viewport - rowWidth} 로 줄었다");
        }
        else
        {
            Assert.True(rowWidth > viewport, "최소 폭에서 멈췄으면 가로 스크롤이어야 한다");
        }
    }

    [Fact]
    public void NarrowTileStopsAtTheMinimum()
    {

        Assert.Equal(
            ColumnLayout.MinFlexWidth,
            ColumnLayout.FlexWidth(243, 220, ColumnLayout.RightMargin, ColumnLayout.MinFlexWidth));

        var edge = ColumnLayout.MinFlexWidth + 220 + ColumnLayout.RightMargin + ColumnLayout.RowChrome;
        Assert.Equal(ColumnLayout.MinFlexWidth, ColumnLayout.FlexWidth(edge, 220, 32, 160));
        Assert.Equal(ColumnLayout.MinFlexWidth, ColumnLayout.FlexWidth(edge - 1, 220, 32, 160));
        Assert.Equal(ColumnLayout.MinFlexWidth + 1, ColumnLayout.FlexWidth(edge + 1, 220, 32, 160));
    }

    [Fact]
    public void SearchViewHasItsOwnFixedSum()
    {
        Assert.Equal(542, ColumnLayout.FlexWidth(1000, 420, 32, 160));

        Assert.Equal(160, ColumnLayout.FlexWidth(503, 420, 32, 160));
    }

    [Fact]
    public void TheResultIsAWholeNumber()
    {
        var width = ColumnLayout.FlexWidth(427.6, 220.4, 32, 160);

        Assert.Equal(width, Math.Floor(width));
        Assert.Equal(169, width);
    }

    [Fact]
    public void BeforeLayoutItFallsBackToTheMinimum()
    {
        Assert.Equal(160, ColumnLayout.FlexWidth(0, 220, 32, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(double.NaN, 220, 32, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(-5, 220, 32, 160));
    }

    [Fact]
    public void ANonFiniteSumFallsBackToTheMinimum()
    {
        Assert.Equal(160, ColumnLayout.FlexWidth(427, double.NaN, 32, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(427, 220, double.NaN, 160));
        Assert.Equal(160, ColumnLayout.FlexWidth(double.PositiveInfinity, 220, 32, 160));
    }

    [Fact]
    public void TheChosenConstants()
    {
        Assert.Equal(32, ColumnLayout.RightMargin);
        Assert.Equal(160, ColumnLayout.MinFlexWidth);
        Assert.Equal(6, ColumnLayout.RowChrome);
    }
}
