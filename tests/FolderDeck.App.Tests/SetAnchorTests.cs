using FolderDeck.Core.Models;

namespace FolderDeck.App.Tests;

public sealed class SetAnchorTests
{

    private static async Task<Workspace> ReloadAsync(
        MainWindowFixture f, Guid id, Func<Workspace, bool> ready)
    {
        f.Store.Flush();

        Workspace? last = null;

        for (var i = 0; i < 40; i++)
        {
            if (f.Store.LoadWorkspace(id).Value is { } workspace)
            {
                last = workspace;
                if (ready(workspace))
                {
                    return workspace;
                }
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException(
            last is null ? "저장 파일이 안 나타났다" : "저장 파일이 바뀐 값을 안 담았다");
    }

    [Fact]
    public async Task ReplacesThePathAndKeepsTheFolderId()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var row = f.Row("작업");
        var id = row.Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, row.Entry.Path);

        Assert.Equal(id, row.Entry.Id);

        Assert.Equal(5, f.ViewModel.Rows.Count);
        Assert.Equal(5, f.Workspace.Folders.Count);
    }

    [Fact]
    public async Task BothLayersMove()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Views"));

        var expected = Path.Combine(f.WorkPath, "Views");
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, f.ViewModel.Rows.Single(r => r.Entry.Id == id).PathText);
    }

    [Fact]
    public async Task TheTileLandsOnTheNewAnchor()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);
        Assert.Contains(panel.DisplayItems, i => i.Name == "Recipe.cs");
    }

    [Fact]
    public async Task CurrentPathNeverReachesTheFile()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");

        await panel.GoUpAsync();
        Assert.True(panel.IsAwayFromAnchor);

        await ReloadAsync(f, f.Workspace.Id, w => w.Folders.Any(e => e.Path == expected));
        var json = await File.ReadAllTextAsync(f.Paths.WorkspaceFile(f.Workspace.Id));

        Assert.DoesNotContain("currentPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Recipe", json);
    }

    [Fact]
    public async Task SurvivesAReload()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var expected = Path.Combine(f.WorkPath, "Recipe");
        var reloaded = await ReloadAsync(
            f, f.Workspace.Id, w => w.Folders.Single(e => e.Id == id).Path == expected);

        var entry = reloaded.Folders.Single(e => e.Id == id);
        Assert.Equal(expected, entry.Path);
        Assert.Equal(id, entry.Id);

        Assert.Equal(5, reloaded.Folders.Count);
    }

    [Fact]
    public async Task YesRenamesTheDisplayNameToTheLeaf()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var entry = f.Workspace.Folders.Single(e => e.Id == id);
        Assert.Equal("Recipe", entry.DisplayName);
        Assert.Equal("Recipe", panel.AnchorName);

        Assert.Single(f.Prompt.Messages);
    }

    [Fact]
    public async Task NoKeepsTheOldDisplayName()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = false;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        var entry = f.Workspace.Folders.Single(e => e.Id == id);
        Assert.Equal("작업", entry.DisplayName);

        Assert.Equal(Path.Combine(f.WorkPath, "Recipe"), entry.Path);
    }

    [Fact]
    public async Task DescriptionIsNeverTouched()
    {
        using var f = new MainWindowFixture();
        f.Prompt.Answer = true;
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Recipe"));

        Assert.Equal("탐색용", f.Workspace.Folders.Single(e => e.Id == id).Description);
        Assert.Empty(f.FolderEditor.Opened);
    }

    [Fact]
    public async Task RejectsAPathAnotherEntryAlreadyHas()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.GoUpAsync();
        var before = f.Row("작업").Entry.Path;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "code"));

        Assert.Equal(before, f.Row("작업").Entry.Path);
        Assert.True(f.ViewModel.MessageIsWarning);
        Assert.Contains("이미 등록된 폴더다", f.ViewModel.Message);

        Assert.Empty(f.Prompt.Messages);
        Assert.NotEqual(f.CodePath, panel.CurrentPath);
    }

    [Fact]
    public async Task RejectsTheAnchorItself()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.GoUpAsync();

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "work"));

        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
        Assert.Contains("이미 앵커다", f.ViewModel.Message);
        Assert.Empty(f.Prompt.Messages);
    }

    [Fact]
    public async Task FilesAreIgnored()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(
            panel.DisplayItems.Single(i => i.Name == "Main.cs"));

        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
        Assert.Empty(f.Prompt.Messages);
    }

    [Fact]
    public async Task NullAndEmptyTileAreIgnored()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;

        await panel.SetAnchorToItemCommand.ExecuteAsync(null);

        Assert.Null(panel.Entry);
        Assert.Empty(f.Prompt.Messages);
    }

    [Fact]
    public async Task WorksOnAPinnedTileToo()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();

        var pinned = f.ViewModel.Tiles
            .Single(t => t.IsPinned && t.Panel.Entry is not null && t.Panel.Entry.Path == f.CodePath)
            .Panel;

        var id = pinned.Entry!.Id;

        await pinned.SetAnchorToItemCommand.ExecuteAsync(
            pinned.DisplayItems.Single(i => i.Name == "Views"));

        var expected = Path.Combine(f.CodePath, "Views");
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, pinned.CurrentPath);

        Assert.Equal(f.WorkPath, f.Row("작업").Entry.Path);
    }
}
