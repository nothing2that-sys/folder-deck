using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class PanelHeaderTests
{

    [Fact]
    public async Task TheCountOnTheNameLineCarriesNoSeparator()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Equal("3개", code.ItemCountText);
        Assert.Equal("3개 · ", code.MetaCountText);
    }

    [Fact]
    public async Task TheMetaLineStillJoinsAndStillAgreesWithTheCount()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Equal(code.MetaText, code.MetaCountText + code.ViewModeText + code.MetaSortText);
        Assert.StartsWith(code.ItemCountText, code.MetaText);
    }

    [Fact]
    public async Task TheCountFollowsTheList()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.NavigateToCommand.ExecuteAsync(System.IO.Path.Combine(f.CodePath, "Recipe"));

        Assert.Contains(nameof(FolderPanelViewModel.ItemCountText), seen);
        Assert.Equal("1개", code.ItemCountText);
    }

    [Fact]
    public async Task NothingSelectedMeansNoSuffix()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.False(code.HasSelection);
        Assert.Equal(string.Empty, code.SelectionSuffix);
    }

    [Fact]
    public async Task TheSuffixCarriesItsOwnSeparator()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [.. code.Items.Take(2)];

        Assert.Equal("2개 선택", code.SelectionText);
        Assert.Equal(" · 2개 선택", code.SelectionSuffix);

        Assert.Equal("3개 · 2개 선택", code.ItemCountText + code.SelectionSuffix);
    }

    [Fact]
    public async Task TheSuffixIsAnnouncedWhenTheSelectionChanges()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.SelectedItems = [code.Items[0]];

        Assert.Contains(nameof(FolderPanelViewModel.SelectionSuffix), seen);
    }

    [Fact]
    public async Task ClearingTheSelectionShortensTheLineInsteadOfRemovingIt()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SelectedItems = [code.Items[0]];
        Assert.NotEqual(string.Empty, code.SelectionSuffix);
        Assert.False(code.HasStatusLine);

        code.ClearSelection();

        Assert.Equal(string.Empty, code.SelectionSuffix);
        Assert.False(code.HasStatusLine);
    }

    [Fact]
    public async Task LeavingTheAnchorStillUsesTheSecondLine()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.NavigateToCommand.ExecuteAsync(System.IO.Path.Combine(f.CodePath, "Recipe"));

        Assert.True(code.IsAwayFromAnchor);
        Assert.True(code.HasStatusLine);

        code.SelectedItems = [code.Items[0]];
        Assert.True(code.HasStatusLine);
    }

    [Fact]
    public async Task TheViewModeIconSaysWhichViewIsOn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.True(code.IsDetailsView);
        Assert.Equal("▦", code.ViewModeIcon);

        code.ToggleViewModeCommand.Execute(null);

        Assert.True(code.IsLargeIconView);
        Assert.Equal("▩", code.ViewModeIcon);

        code.ToggleViewModeCommand.Execute(null);

        Assert.True(code.IsExtraLargeIconView);
        Assert.Equal("▣", code.ViewModeIcon);

        code.ToggleViewModeCommand.Execute(null);

        Assert.True(code.IsListView);
        Assert.Equal("☰", code.ViewModeIcon);
    }

    [Fact]
    public async Task TheViewModeTipNamesBothStates()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.Contains("지금 details", code.ViewModeTip);
        Assert.Contains("큰 아이콘", code.ViewModeTip);

        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains("지금 큰 아이콘", code.ViewModeTip);
        Assert.Contains("아주 큰 아이콘", code.ViewModeTip);

        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains("지금 아주 큰 아이콘", code.ViewModeTip);
        Assert.Contains("list", code.ViewModeTip);

        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains("지금 list", code.ViewModeTip);
        Assert.Contains("details", code.ViewModeTip);
    }

    [Fact]
    public async Task TheViewModeIconIsAnnouncedWhenItFlips()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.ToggleViewModeCommand.Execute(null);

        Assert.Contains(nameof(FolderPanelViewModel.ViewModeIcon), seen);
        Assert.Contains(nameof(FolderPanelViewModel.ViewModeTip), seen);
    }

    [Fact]
    public async Task TheSortIconIsADirectionTriangle()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        Assert.Equal("▴", f.ViewModel.Panels[0].SortIcon);

        Assert.Equal("▾", f.ViewModel.Panels[1].SortIcon);
    }

    [Fact]
    public async Task TheSortIconFlipsWithTheDirection()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Equal("▴", code.SortIcon);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Equal("▾", code.SortIcon);
    }

    [Fact]
    public async Task TurningOffFoldersFirstShowsUpOnTheSortIcon()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal("▴", code.SortIcon);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal("∪▴", code.SortIcon);
        Assert.Contains("통합", code.SortTip);

        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal("▴", code.SortIcon);
        Assert.DoesNotContain("통합", code.SortTip);
    }

    [Fact]
    public async Task TheMergedMarkAndTheDirectionBothShow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        Assert.True(output.Entry!.SortDesc);

        await output.ToggleFoldersFirstCommand.ExecuteAsync(null);

        Assert.Equal("∪▾", output.SortIcon);
    }

    [Fact]
    public async Task TheSortTipNamesTheCriterion()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Contains("이름 ↑", code.SortTip);

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.Contains("수정 ↑", code.SortTip);

        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.Contains("수정 ↓", code.SortTip);
    }

    [Fact]
    public async Task TheIconAndTheMetaTextAgree()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        foreach (var sort in new[] { SortBy.Name, SortBy.Type, SortBy.Type, SortBy.Size })
        {
            await code.SetSortCommand.ExecuteAsync(sort);

            var descending = code.MetaSortText.Contains('↓');
            Assert.Equal(descending ? "▾" : "▴", code.SortIcon);
        }
    }

    [Fact]
    public async Task TheSortIconIsAnnouncedWhenItChanges()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Contains(nameof(FolderPanelViewModel.SortIcon), seen);
        Assert.Contains(nameof(FolderPanelViewModel.SortTip), seen);

        seen.Clear();
        await code.ToggleFoldersFirstCommand.ExecuteAsync(null);
        Assert.Contains(nameof(FolderPanelViewModel.SortIcon), seen);
        Assert.Contains(nameof(FolderPanelViewModel.SortTip), seen);
    }

    [Fact]
    public async Task TheStatusLineIsGoneWhenNothingIsHappening()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        Assert.False(code.IsAwayFromAnchor);
        Assert.False(code.HasSelection);
        Assert.False(code.IsLoading);
        Assert.False(code.HasStatusLine);
    }

    [Fact]
    public async Task SelectingSomethingDoesNotChangeTheHeaderHeight()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.HasStatusLine);

        code.SelectedItems = [code.Items[0]];

        Assert.True(code.HasSelection);
        Assert.False(code.HasStatusLine);

        code.SelectedItems = [];
        Assert.False(code.HasStatusLine);
    }

    [Fact]
    public async Task LeavingTheAnchorBringsTheStatusLineBack()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.HasStatusLine);

        await code.NavigateToCommand.ExecuteAsync(System.IO.Path.Combine(f.CodePath, "Recipe"));

        Assert.True(code.IsAwayFromAnchor);
        Assert.True(code.HasStatusLine);

        await code.GoToAnchorCommand.ExecuteAsync(null);

        Assert.False(code.HasStatusLine);
    }

    [Fact]
    public void LoadingBringsTheStatusLineBack()
    {
        using var f = new MainWindowFixture();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        code.IsLoading = true;

        Assert.True(code.HasStatusLine);
        Assert.Contains(nameof(FolderPanelViewModel.HasStatusLine), seen);
    }
}
