using FolderDeck.App.Services;
using FolderDeck.App.ViewModels;
using FolderDeck.Core.Enumeration;

namespace FolderDeck.App.Tests;






public sealed class CurrentFolderMenuSearchGuardTests
{

    private static FolderPanelViewModel BareSearchPanel(List<string> rejections) => new(
        new FolderEnumerator(),
        new FakeShellLauncher(),
        new FakeClipboardService(),
        isRotating: false,
        isSearchTile: true,
        reportError: null,
        reportRejection: rejections.Add);



    [Fact]
    public void CreateNewFolderIsRejectedOnASearchTile()
    {
        var rejections = new List<string>();
        var panel = BareSearchPanel(rejections);

        panel.CreateNewFolderCommand.Execute("새 폴더");

        Assert.Single(rejections);
    }



    [Fact]
    public async Task SetAnchorToCurrentIsRejectedOnASearchTile()
    {
        var rejections = new List<string>();
        var panel = BareSearchPanel(rejections);
        var invoked = false;
        panel.SetAnchorFolderToPath = (_, _) =>
        {
            invoked = true;
            return true;
        };

        await panel.SetAnchorToCurrentCommand.ExecuteAsync(null);

        Assert.Single(rejections);
        Assert.False(invoked);
    }







    [Fact]
    public void TheGuardChecksIsSearchTileNotCurrentPath()
    {
        var rejections = new List<string>();
        var panel = BareSearchPanel(rejections);
        panel.CurrentPath = Path.Combine(Path.GetTempPath(), "이미-값이-있다");

        panel.RegisterCurrentFolderCommand.Execute(null);

        Assert.Single(rejections);
    }
}
