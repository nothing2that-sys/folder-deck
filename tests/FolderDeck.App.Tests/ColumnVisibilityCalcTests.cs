using System.Windows.Controls;
using FolderDeck.App.Views;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;












public sealed class ColumnVisibilityCalcTests
{
    private static GridViewColumn Column(SortBy? sortKey)
    {
        var column = new GridViewColumn();
        ColumnSort.SetSortKey(column, sortKey);
        return column;
    }


    [Fact]
    public void TheNameColumnAlwaysShows()
    {
        var name = Column(SortBy.Name);

        Assert.True(ColumnVisibility.ShouldShow(name, showSize: false, showModified: false, showPosition: false));
        Assert.True(ColumnVisibility.ShouldShow(name, showSize: true, showModified: true, showPosition: true));
    }


    [Fact]
    public void TheSizeColumnFollowsOnlyShowSize()
    {
        var size = Column(SortBy.Size);

        Assert.True(ColumnVisibility.ShouldShow(size, showSize: true, showModified: false, showPosition: false));
        Assert.False(ColumnVisibility.ShouldShow(size, showSize: false, showModified: true, showPosition: true));
    }


    [Fact]
    public void TheModifiedColumnFollowsOnlyShowModified()
    {
        var modified = Column(SortBy.Modified);

        Assert.True(ColumnVisibility.ShouldShow(modified, showSize: false, showModified: true, showPosition: false));
        Assert.False(ColumnVisibility.ShouldShow(modified, showSize: true, showModified: false, showPosition: true));
    }





    [Fact]
    public void TheUnkeyedColumnIsThePositionColumnAndFollowsShowPosition()
    {
        var position = Column(sortKey: null);

        Assert.True(ColumnVisibility.ShouldShow(position, showSize: false, showModified: false, showPosition: true));
        Assert.False(ColumnVisibility.ShouldShow(position, showSize: true, showModified: true, showPosition: false));
    }


    [Fact]
    public void HeaderTextDoesNotAffectTheVerdict()
    {
        var size = Column(SortBy.Size);
        size.Header = "이 열의 이름을 뭐라 지어도";

        Assert.False(ColumnVisibility.ShouldShow(size, showSize: false, showModified: true, showPosition: true));
    }
}
