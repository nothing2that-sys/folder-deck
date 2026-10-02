using FolderDeck.App.ViewModels;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class ColumnHeaderSortTests
{

    [Fact]
    public async Task OnlyTheSortedColumnCarriesTheArrow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.Equal(SortBy.Name, code.Entry!.SortBy);

        Assert.Equal("이름 ↑", code.NameHeaderText);
        Assert.Equal("크기", code.SizeHeaderText);
        Assert.Equal("수정", code.ModifiedHeaderText);
    }

    [Fact]
    public async Task TheArrowMovesWithTheSortColumn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.SetSortCommand.ExecuteAsync(SortBy.Modified);

        Assert.Equal("이름", code.NameHeaderText);
        Assert.Equal("크기", code.SizeHeaderText);
        Assert.Equal("수정 ↑", code.ModifiedHeaderText);
    }

    [Fact]
    public async Task ClickingTheSameColumnFlipsTheArrow()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        await code.SetSortCommand.ExecuteAsync(SortBy.Size);
        Assert.Equal("크기 ↑", code.SizeHeaderText);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.True(code.Entry!.SortDesc);
        Assert.Equal("크기 ↓", code.SizeHeaderText);
    }

    [Fact]
    public async Task TheHeaderAndTheMetaLineAgree()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        foreach (var sort in new[] { SortBy.Name, SortBy.Size, SortBy.Modified, SortBy.Size })
        {
            await code.SetSortCommand.ExecuteAsync(sort);

            var header = code.Entry!.SortBy switch
            {
                SortBy.Size => code.SizeHeaderText,
                SortBy.Modified => code.ModifiedHeaderText,
                _ => code.NameHeaderText,
            };

            Assert.Equal(code.SortLabel + code.SortArrow, header);
            Assert.Contains(code.SortLabel + code.SortArrow, code.MetaSortText);
        }
    }

    [Fact]
    public async Task NotificationsAccompanyTheHeaderChange()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        await code.SetSortCommand.ExecuteAsync(SortBy.Size);

        Assert.Contains(nameof(FolderPanelViewModel.NameHeaderText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.SizeHeaderText), seen);
        Assert.Contains(nameof(FolderPanelViewModel.ModifiedHeaderText), seen);
    }

    [Fact]
    public async Task SwitchingFoldersRedrawsTheHeaders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        Assert.Equal("이름 ↑", panel.NameHeaderText);

        await panel.SetSortCommand.ExecuteAsync(SortBy.Modified);
        Assert.Equal("수정 ↑", panel.ModifiedHeaderText);

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));

        Assert.Equal("이름 ↑", panel.NameHeaderText);
        Assert.Equal("수정", panel.ModifiedHeaderText);
    }

    [Fact]
    public void AnEmptyPanelStillHasPlainHeaders()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);

        Assert.Equal("이름", rotating.NameHeaderText);
        Assert.Equal("크기", rotating.SizeHeaderText);
        Assert.Equal("수정", rotating.ModifiedHeaderText);
    }
}
