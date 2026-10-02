using FolderDeck.App.ViewModels;

namespace FolderDeck.App.Tests;





public sealed class NewFolderNameSuggestionTests : IDisposable
{
    private readonly string _root;

    public NewFolderNameSuggestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FolderDeck.NewFolderNameTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {

        }
    }


    [Fact]
    public void SuggestsPlainNameWhenFree() =>
        Assert.Equal("새 폴더", FolderPanelViewModel.SuggestNewFolderName(_root));


    [Fact]
    public void SuggestsNumberedNameWhenTaken()
    {
        Directory.CreateDirectory(Path.Combine(_root, "새 폴더"));

        Assert.Equal("새 폴더 (2)", FolderPanelViewModel.SuggestNewFolderName(_root));
    }
}
