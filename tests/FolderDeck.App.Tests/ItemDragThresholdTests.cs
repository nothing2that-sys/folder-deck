using System.Windows;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

public sealed class ItemDragThresholdTests
{

    [Fact]
    public void NotMovingFarEnoughIsNotPastTheThreshold()
    {
        var origin = new Point(100, 100);

        Assert.False(ItemDragThreshold.PastThreshold(origin, origin));
        Assert.False(ItemDragThreshold.PastThreshold(origin, new Point(100.5, 100.5)));
    }

    [Fact]
    public void CrossingTheThresholdOnEitherAxisIsPastTheThreshold()
    {
        var origin = new Point(100, 100);
        var h = SystemParameters.MinimumHorizontalDragDistance;
        var v = SystemParameters.MinimumVerticalDragDistance;

        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100 + h, 100)));
        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100, 100 + v)));

        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100 - h, 100)));
        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100, 100 - v)));
    }

    [Fact]
    public void TheExactThresholdValueIsAlreadyPastItButOneLessIsNot()
    {
        var origin = new Point(100, 100);
        var h = SystemParameters.MinimumHorizontalDragDistance;
        var v = SystemParameters.MinimumVerticalDragDistance;

        Assert.True(ItemDragThreshold.PastThreshold(origin, new Point(100 + h, 100 + v)));
        Assert.False(ItemDragThreshold.PastThreshold(origin, new Point(100 + h - 0.01, 100 + v - 0.01)));
    }
}
