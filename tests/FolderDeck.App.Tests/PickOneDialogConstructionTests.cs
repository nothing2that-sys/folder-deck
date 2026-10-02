using System.Windows.Controls;
using FolderDeck.App.Views;

namespace FolderDeck.App.Tests;

public sealed class PickOneDialogConstructionTests
{
    [Fact]
    public void ConstructingWithOptionsDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            _ = new PickOneDialog("제목", "안내", ["산출물"]);
        });
    }

    [Fact]
    public void FirstOptionIsSelectedByDefault()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var dialog = new PickOneDialog("제목", "안내", ["산출물", "작업"]);
            var list = (ListBox)dialog.FindName("OptionList")!;

            Assert.Equal(0, list.SelectedIndex);
        });
    }

    [Fact]
    public void NoOptionsLeavesNothingSelectedButDoesNotThrow()
    {
        StaWpfTestSupport.RunOnSta(() =>
        {
            var dialog = new PickOneDialog("제목", "안내", []);
            var list = (ListBox)dialog.FindName("OptionList")!;

            Assert.Equal(-1, list.SelectedIndex);
        });
    }
}
