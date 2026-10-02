using System.Linq;
using System.Windows.Controls;
using FolderDeck.App.Views;
using FolderDeck.Core.Comparison;

namespace FolderDeck.App.Tests;

public sealed class ContentDiffWindowConstructionTests
{
    [Fact]
    public void ConstructingWithNoDifferencesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a\nb", "a\nb");
            _ = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });
        });
    }

    [Fact]
    public void ConstructingWithLineLevelDifferencesDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a\nb", "a\nb\nc");
            _ = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });
        });
    }

    [Fact]
    public void ModifiedLineProducesMultipleSpansNotOneWholeLineSpan()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("the quick fox", "the slow fox");
            var window = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });
            var leftList = (ItemsControl)window.FindName("LeftList")!;
            var rightList = (ItemsControl)window.FindName("RightList")!;

            var leftRow = (ContentDiffRow)Assert.Single(leftList.Items.Cast<object>());
            var rightRow = (ContentDiffRow)Assert.Single(rightList.Items.Cast<object>());

            Assert.True(leftRow.LeftSpans.Count > 1);
            Assert.True(rightRow.RightSpans.Count > 1);
            Assert.Contains(leftRow.LeftSpans, s => s.Background != System.Windows.Media.Brushes.Transparent);
        });
    }

    [Fact]
    public void HeadersShowTheActualFilePath()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var diff = FileContentDiffer.Diff("a", "a");
            var window = new ContentDiffWindow(
                "shared.txt", "코드", @"C:\code\shared.txt", "산출물", @"C:\out\shared.txt", diff, _ => { });

            var leftHeader = (TextBlock)window.FindName("LeftHeaderText")!;
            var rightHeader = (TextBlock)window.FindName("RightHeaderText")!;

            Assert.Contains(@"C:\code\shared.txt", leftHeader.Text);
            Assert.Contains(@"C:\out\shared.txt", rightHeader.Text);
        });
    }

}
