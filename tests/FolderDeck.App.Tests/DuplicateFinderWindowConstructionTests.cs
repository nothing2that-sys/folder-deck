using System.Windows.Controls;
using System.Windows.Data;
using FolderDeck.App.Views;
using FolderDeck.Core.Comparison;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;

public sealed class DuplicateFinderWindowConstructionTests
{
    [Fact]
    public void ConstructingWithNoGroupsDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var window = new DuplicateFinderWindow([], _ => { });
            var list = (ListView)window.FindName("ResultList")!;

            Assert.Empty(list.Items);
        });
    }

    [Fact]
    public void ConstructingWithGroupsFlattensEntriesIntoRows()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var groups = new[]
            {
                new DuplicateGroup("a.txt", 10, [
                    new DuplicateGroupEntry("코드", new FolderItem("a.txt", @"C:\code\a.txt", false, 10, DateTime.UtcNow)),
                    new DuplicateGroupEntry("산출물", new FolderItem("a.txt", @"C:\out\a.txt", false, 10, DateTime.UtcNow)),
                ]),
            };

            var window = new DuplicateFinderWindow(groups, _ => { });
            var list = (ListView)window.FindName("ResultList")!;

            Assert.Equal(2, list.Items.Count);
        });
    }

    [Fact]
    public void GroupsWithTheSameNameButDifferentExactSizeStayDistinctInTheView()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var groups = new[]
            {
                new DuplicateGroup("a.txt", 1024, [
                    new DuplicateGroupEntry("코드", new FolderItem("a.txt", @"C:\code\a.txt", false, 1024, DateTime.UtcNow)),
                    new DuplicateGroupEntry("산출물", new FolderItem("a.txt", @"C:\out\a.txt", false, 1024, DateTime.UtcNow)),
                ]),
                new DuplicateGroup("a.txt", 1025, [
                    new DuplicateGroupEntry("작업", new FolderItem("a.txt", @"C:\work\a.txt", false, 1025, DateTime.UtcNow)),
                    new DuplicateGroupEntry("문서", new FolderItem("a.txt", @"C:\docs\a.txt", false, 1025, DateTime.UtcNow)),
                ]),
            };

            var window = new DuplicateFinderWindow(groups, _ => { });
            var list = (ListView)window.FindName("ResultList")!;
            var view = CollectionViewSource.GetDefaultView(list.ItemsSource);

            Assert.Equal(4, list.Items.Count);
            Assert.Equal(2, view.Groups!.Count);
        });
    }

}
