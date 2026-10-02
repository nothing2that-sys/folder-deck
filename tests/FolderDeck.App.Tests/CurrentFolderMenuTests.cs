namespace FolderDeck.App.Tests;







public sealed class CurrentFolderMenuTests
{



    [Fact]
    public async Task GivenAPathTheEntryPathChangesAndSaves()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;
        var id = f.Row("작업").Entry.Id;


        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        await panel.SetAnchorToCurrentCommand.ExecuteAsync(null);

        var expected = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(expected, f.Workspace.Folders.Single(e => e.Id == id).Path);
        Assert.Equal(expected, panel.CurrentPath);
        Assert.False(panel.IsAwayFromAnchor);


        f.Store.Flush();
        var reloaded = f.Store.LoadWorkspace(f.Workspace.Id).ValueOrThrow();
        Assert.Equal(expected, reloaded.Folders.Single(e => e.Id == id).Path);
    }



    [Fact]
    public async Task TheExistingItemBasedCommandStillWorks()
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
        Assert.Equal(expected, panel.CurrentPath);
    }



    [Fact]
    public async Task RejectsWhenThePanelHasNoEntry()
    {
        using var f = new MainWindowFixture();
        var panel = f.Rotating;


        panel.CurrentPath = f.WorkPath;

        await panel.SetAnchorToCurrentCommand.ExecuteAsync(null);

        Assert.Null(panel.Entry);
        Assert.Equal(5, f.Workspace.Folders.Count);
    }





    [Fact]
    public async Task RegisteringTheCurrentFolderAddsOneToFolders()
    {
        using var f = new MainWindowFixture();
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ShowFolderAsync(f.Row("작업"));
        var panel = f.Rotating;

        await panel.OpenCommand.ExecuteAsync(panel.DisplayItems.Single(i => i.Name == "Recipe"));

        panel.RegisterCurrentFolderCommand.Execute(null);

        var target = Path.Combine(f.WorkPath, "Recipe");
        Assert.Equal(6, f.Workspace.Folders.Count);
        Assert.Contains(f.Workspace.Folders, e => e.Path == target);
    }
}
