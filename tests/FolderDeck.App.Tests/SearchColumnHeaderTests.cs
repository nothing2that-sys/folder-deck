using FolderDeck.App.Services;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;






public sealed class SearchColumnHeaderTests
{
    private static FolderPanelViewModel MakePanel(bool isSearchTile) => new(
        new FolderEnumerator(), new FakeShellLauncher(), new FakeClipboardService(),
        isRotating: false, isSearchTile);




    [Fact]
    public void SearchTileNameHeaderShowsAnArrowByDefault()
    {
        var panel = MakePanel(isSearchTile: true);

        Assert.Equal("이름 ↑", panel.NameHeaderText);
    }


    [Fact]
    public async Task SettingSortToSizeMovesTheArrowToTheSizeHeader()
    {
        var panel = MakePanel(isSearchTile: true);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal("크기 ↑", panel.SizeHeaderText);
        Assert.Equal("이름", panel.NameHeaderText);
    }


    [Fact]
    public async Task FlippingTheDirectionChangesTheArrowShape()
    {
        var panel = MakePanel(isSearchTile: true);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Equal("크기 ↓", panel.SizeHeaderText);
    }




    [Fact]
    public async Task RecursiveSearchShowsNoArrowOnAnyHeader()
    {
        using var f = new MainWindowFixture();
        f.MakeDeepTree();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        panel.IncludeSubfolders = true;
        panel.SearchText = "NumberBox.xaml";
        for (var i = 0; i < 200 && (panel.IsSearching || panel.DisplayItems.Count == 0); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(panel.IsRecursiveSearch);
        Assert.Equal("이름", panel.NameHeaderText);
        Assert.Equal("크기", panel.SizeHeaderText);
        Assert.Equal("수정", panel.ModifiedHeaderText);
    }


    [Fact]
    public async Task AnOrdinaryFolderListStillShowsArrowsAsBefore()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        var panel = f.ViewModel.Panels[0];

        Assert.Equal("이름 ↑", panel.NameHeaderText);
    }



    [Fact]
    public void AnEmptyRotatingPanelStillHasPlainHeaders()
    {
        using var f = new MainWindowFixture();
        var rotating = f.Rotating;

        Assert.True(rotating.IsEmpty);
        Assert.Equal("이름", rotating.NameHeaderText);
        Assert.Equal("크기", rotating.SizeHeaderText);
        Assert.Equal("수정", rotating.ModifiedHeaderText);
    }








    [Fact]
    public async Task EachColumnOnlyGetsItsOwnArrow()
    {
        static bool HasArrow(string text) => text.Contains('↑') || text.Contains('↓');

        var panel = MakePanel(isSearchTile: true);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Name);
        Assert.True(HasArrow(panel.NameHeaderText));
        Assert.False(HasArrow(panel.SizeHeaderText));
        Assert.False(HasArrow(panel.ModifiedHeaderText));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.False(HasArrow(panel.NameHeaderText));
        Assert.True(HasArrow(panel.SizeHeaderText));
        Assert.False(HasArrow(panel.ModifiedHeaderText));

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.False(HasArrow(panel.NameHeaderText));
        Assert.False(HasArrow(panel.SizeHeaderText));
        Assert.True(HasArrow(panel.ModifiedHeaderText));
    }




    [Fact]
    public void SortByHasNoLocationOrPathMember()
    {
        var names = Enum.GetNames<SortBy>();

        Assert.Equal(["Name", "Modified", "Size", "Type"], names);
    }
}
