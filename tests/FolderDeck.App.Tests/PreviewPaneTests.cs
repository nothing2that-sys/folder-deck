using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;
using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class PreviewPaneTests
{

    private static FolderPanelViewModel BareSearchPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: null);

    private static FolderPanelViewModel BareFolderPanel() => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: false,
        reportError: null,
        reportRejection: null);

    private static void Select(FolderPanelViewModel panel, params string[] names) =>
        panel.SelectedItems = [.. panel.Items.Where(i => names.Contains(i.Name))];

    [Fact]
    public async Task TurningOnShowPreviewFlipsTheFolderEntryAndSavesOnce()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Assert.False(code.Entry!.ShowPreview);

        var saves = 0;
        var save = code.SaveFolderEntry!;
        code.SaveFolderEntry = () =>
        {
            saves++;
            save();
        };

        code.ToggleShowPreviewCommand.Execute(null);

        Assert.True(code.Entry!.ShowPreview);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task TogglingPreviewBackTurnsItOff()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];

        code.ToggleShowPreviewCommand.Execute(null);
        code.ToggleShowPreviewCommand.Execute(null);

        Assert.False(code.Entry!.ShowPreview);
    }

    [Fact]
    public async Task ThePreviewChoiceTravelsWithTheFolderInARotatingSlot()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        Assert.False(panel.Entry!.ShowPreview);

        panel.ToggleShowPreviewCommand.Execute(null);
        Assert.True(panel.Entry!.ShowPreview);

        await f.ViewModel.ShowFolderAsync(f.Row("문서"));
        Assert.False(panel.Entry!.ShowPreview);

        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        Assert.True(panel.Entry!.ShowPreview);
    }

    [Fact]
    public async Task EachTileTogglesPreviewOnItsOwn()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        f.ViewModel.Panels[0].ToggleShowPreviewCommand.Execute(null);

        Assert.True(f.ViewModel.Panels[0].Entry!.ShowPreview);
        Assert.False(f.ViewModel.Panels[1].Entry!.ShowPreview);
    }

    [Fact]
    public async Task ThePreviewChoiceSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.ToggleShowPreviewCommand.Execute(null);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.True(entry.ShowPreview);

        Assert.True(entry.ShowSize);
    }

    [Fact]
    public void AnEmptyPanelHasNothingToTogglePreview()
    {
        using var f = new MainWindowFixture();

        var rotating = f.Rotating;
        Assert.True(rotating.IsEmpty);
        Assert.Null(rotating.Entry);

        Assert.False(rotating.CurrentShowPreview);

        rotating.ToggleShowPreviewCommand.Execute(null);

        Assert.True(rotating.IsEmpty);
    }

    [Fact]
    public void CurrentShowPreviewFollowsTheFolderEntry()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };

        Assert.False(panel.CurrentShowPreview);

        panel.Entry.ShowPreview = true;

        Assert.True(panel.CurrentShowPreview);
    }

    [Fact]
    public void TogglingPreviewOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.ToggleShowPreviewCommand.Execute(null);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);

        Assert.True(panel.CurrentShowPreview);
    }

    [Fact]
    public void TogglingPreviewOnASearchTileFlipsCurrentShowPreview()
    {
        var panel = BareSearchPanel();
        Assert.False(panel.CurrentShowPreview);

        panel.ToggleShowPreviewCommand.Execute(null);
        Assert.True(panel.CurrentShowPreview);

        panel.ToggleShowPreviewCommand.Execute(null);
        Assert.False(panel.CurrentShowPreview);
    }

    [Fact]
    public void SettingThePreviewRatioFlipsTheFolderEntryAndSavesOnce()
    {
        var panel = BareFolderPanel();
        panel.Entry = new FolderEntry { Path = @"D:\x" };
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.SetPreviewRatio(0.6);

        Assert.Equal(0.6, panel.Entry.PreviewRatio);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void AnOutOfRangePreviewRatioFallsBackToTheDefault()
    {
        var panel = BareSearchPanel();

        panel.SetPreviewRatio(1.5);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, panel.CurrentPreviewRatio);

        panel.SetPreviewRatio(-0.2);
        Assert.Equal(FolderEntry.DefaultPreviewRatio, panel.CurrentPreviewRatio);
    }

    [Fact]
    public async Task ThePreviewRatioSurvivesAReopen()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        code.SetPreviewRatio(0.6);

        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        var entry = reloaded.Folders.Single(e => e.Path == f.CodePath);

        Assert.Equal(0.6, entry.PreviewRatio);

        Assert.True(entry.ShowSize);
    }

    [Fact]
    public void SettingThePreviewRatioOnASearchTileTouchesNeitherEntryNorSave()
    {
        var panel = BareSearchPanel();
        var saves = 0;
        panel.SaveFolderEntry = () => saves++;

        panel.SetPreviewRatio(0.6);

        Assert.Null(panel.Entry);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void AssigningTheEntryNotifiesCurrentShowPreview()
    {
        var panel = BareFolderPanel();

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.Entry = new FolderEntry { Path = @"D:\x", ShowPreview = true, PreviewRatio = 0.6 };

        Assert.Contains(nameof(FolderPanelViewModel.CurrentShowPreview), seen);
    }

    [Fact]
    public void AssigningTheEntryNotifiesCurrentPreviewRatio()
    {
        var panel = BareFolderPanel();

        var seen = new List<string?>();
        panel.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        panel.Entry = new FolderEntry { Path = @"D:\x", ShowPreview = true, PreviewRatio = 0.6 };

        Assert.Contains(nameof(FolderPanelViewModel.CurrentPreviewRatio), seen);
    }

    [Fact]
    public async Task ASingleSelectedFileIsThePreviewTarget()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var file = code.Items.First(i => !i.IsDirectory);
        Select(code, file.Name);

        Assert.Same(file, code.PreviewTarget);
    }

    [Fact]
    public async Task SelectingTwoFilesLeavesNoPreviewTarget()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var output = f.ViewModel.Panels[1];
        var files = output.Items.Where(i => !i.IsDirectory).Select(i => i.Name).ToArray();
        Select(output, files);

        Assert.Equal(2, output.SelectedItems.Count);
        Assert.Null(output.PreviewTarget);
    }

    [Fact]
    public async Task SelectingNothingLeavesNoPreviewTarget()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        Select(code);

        Assert.Empty(code.SelectedItems);
        Assert.Null(code.PreviewTarget);
    }

    [Fact]
    public async Task ASingleSelectedFolderIsNotAPreviewTarget()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var folder = code.Items.First(i => i.IsDirectory);
        Select(code, folder.Name);

        Assert.Null(code.PreviewTarget);
    }

    [Fact]
    public async Task AssigningSelectedItemsNotifiesPreviewTarget()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var code = f.ViewModel.Panels[0];
        var file = code.Items.First(i => !i.IsDirectory);

        var seen = new List<string?>();
        code.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        Select(code, file.Name);

        Assert.Contains(nameof(FolderPanelViewModel.PreviewTarget), seen);
    }
}
